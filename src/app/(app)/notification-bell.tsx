"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import {
  getUnreadCountAction,
  listNotificationsAction,
  markNotificationReadAction,
  markAllNotificationsReadAction,
  type NotificationItem,
} from "./notifications-actions";

const POLL_INTERVAL_MS = 20_000;

const TYPE_LABELS: Record<string, string> = {
  ai_suggestion_ready: "AI suggestions are ready to review",
};

function labelFor(notification: NotificationItem) {
  return TYPE_LABELS[notification.type] ?? notification.type;
}

function hrefFor(notification: NotificationItem) {
  if (notification.entityType === "deal" && notification.entityId) {
    return `/pipeline/${notification.entityId}`;
  }
  return null;
}

// Unread count polls in the background (same pattern as every AI-feature
// poll loop in this app); the panel's list is only fetched when opened,
// not continuously, to avoid an unnecessary request every 20s. See the
// notifications-and-digests skill.
export function NotificationBell() {
  const [unreadCount, setUnreadCount] = useState(0);
  const [open, setOpen] = useState(false);
  const [notifications, setNotifications] = useState<NotificationItem[] | null>(null);
  const [loading, setLoading] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    let cancelled = false;
    async function poll() {
      const count = await getUnreadCountAction();
      if (!cancelled) setUnreadCount(count);
    }
    void poll();
    const interval = setInterval(poll, POLL_INTERVAL_MS);
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, []);

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  async function toggleOpen() {
    const next = !open;
    setOpen(next);
    if (next) {
      setLoading(true);
      const list = await listNotificationsAction();
      setNotifications(list);
      setLoading(false);
    }
  }

  async function handleMarkRead(id: string) {
    await markNotificationReadAction(id);
    setNotifications((prev) => prev?.map((n) => (n.id === id ? { ...n, readAt: new Date().toISOString() } : n)) ?? null);
    setUnreadCount((prev) => Math.max(0, prev - 1));
  }

  async function handleMarkAllRead() {
    await markAllNotificationsReadAction();
    setNotifications((prev) => prev?.map((n) => ({ ...n, readAt: n.readAt ?? new Date().toISOString() })) ?? null);
    setUnreadCount(0);
  }

  return (
    <div ref={containerRef} className="relative">
      <button
        type="button"
        onClick={toggleOpen}
        aria-label={`Notifications${unreadCount > 0 ? ` (${unreadCount} unread)` : ""}`}
        className="relative flex h-8 w-8 items-center justify-center rounded-full text-neutral-600 hover:bg-neutral-100"
      >
        <span aria-hidden>🔔</span>
        {unreadCount > 0 ? (
          <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-red-600 px-1 text-[10px] font-medium text-white">
            {unreadCount > 9 ? "9+" : unreadCount}
          </span>
        ) : null}
      </button>

      {open ? (
        <div className="absolute right-0 z-10 mt-2 w-80 rounded-md border border-neutral-200 bg-white shadow-lg">
          <div className="flex items-center justify-between border-b border-neutral-100 px-3 py-2">
            <span className="text-sm font-medium text-neutral-800">Notifications</span>
            {notifications && notifications.some((n) => !n.readAt) ? (
              <button type="button" onClick={handleMarkAllRead} className="text-xs text-neutral-500 hover:text-neutral-900">
                Mark all read
              </button>
            ) : null}
          </div>
          <div className="max-h-80 overflow-y-auto">
            {loading ? <p className="p-3 text-sm text-neutral-500">Loading…</p> : null}
            {!loading && notifications?.length === 0 ? <p className="p-3 text-sm text-neutral-500">No notifications yet.</p> : null}
            {notifications?.map((notification) => {
              const href = hrefFor(notification);
              const body = (
                <div className="flex items-start gap-2 px-3 py-2 text-sm">
                  {!notification.readAt ? <span className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-indigo-600" aria-hidden /> : <span className="mt-1.5 h-1.5 w-1.5 shrink-0" />}
                  <div className="min-w-0 flex-1">
                    <p className="text-neutral-800">{labelFor(notification)}</p>
                    <p className="text-xs text-neutral-500">{new Date(notification.createdAt).toLocaleString()}</p>
                  </div>
                </div>
              );
              return (
                <div key={notification.id} className="border-b border-neutral-50 last:border-0 hover:bg-neutral-50">
                  {href ? (
                    <Link href={href} onClick={() => !notification.readAt && handleMarkRead(notification.id)} className="block">
                      {body}
                    </Link>
                  ) : (
                    <button type="button" className="block w-full text-left" onClick={() => handleMarkRead(notification.id)}>
                      {body}
                    </button>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      ) : null}
    </div>
  );
}
