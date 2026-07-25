using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;
using CrmApi.Services;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class TaskAssignmentAndOverdueSweepTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task CreateTask_WithAssignee_PersistsAssignment()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/tasks",
            new CreateTaskRequest("Follow up", null, null, null, null, ws.User.Id));

        response.EnsureSuccessStatusCode();
        var task = await response.Content.ReadFromJsonAsync<TaskDto>();
        Assert.Equal(ws.User.Id, task!.AssignedToUserId);
    }

    [Fact]
    public async Task CreateTask_WithNonMemberAssignee_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/tasks",
            new CreateTaskRequest("Follow up", null, null, null, null, Guid.NewGuid().ToString("N")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTask_CanReassign()
    {
        var ws = await SeedWorkspaceAsync();
        var task = TestData.TaskItem(ws.Workspace, "Follow up");
        await WithDb(async db => { db.Tasks.Add(task); await db.SaveChangesAsync(); });

        var response = await ws.Client.PutAsJsonAsync($"/api/tasks/{task.Id}",
            new UpdateTaskRequest("Follow up", null, false, ws.User.Id));

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<TaskDto>();
        Assert.Equal(ws.User.Id, updated!.AssignedToUserId);
    }

    [Fact]
    public async Task SweepOverdueTasks_NotifiesAssigneeOfOverdueTask()
    {
        var ws = await SeedWorkspaceAsync();
        var overdueTask = TestData.TaskItem(ws.Workspace, "Overdue thing");
        overdueTask.AssignedToUserId = ws.User.Id;
        overdueTask.DueAt = DateTime.UtcNow.AddDays(-1);
        await WithDb(async db => { db.Tasks.Add(overdueTask); await db.SaveChangesAsync(); });

        await WithDb(async db =>
        {
            var queue = new JobQueueService(db);
            await queue.Enqueue("sweep_overdue_tasks", new { }, workspaceId: null);
        });
        await ProcessAllPendingJobsAsync();

        var notifications = await ws.Client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications");
        Assert.Contains(notifications!, n => n.Type == "task_overdue" && n.EntityId == overdueTask.Id);
    }

    [Fact]
    public async Task SweepOverdueTasks_DoesNotNotifyForTasksNotYetDue()
    {
        var ws = await SeedWorkspaceAsync();
        var futureTask = TestData.TaskItem(ws.Workspace, "Not due yet");
        futureTask.AssignedToUserId = ws.User.Id;
        futureTask.DueAt = DateTime.UtcNow.AddDays(1);
        await WithDb(async db => { db.Tasks.Add(futureTask); await db.SaveChangesAsync(); });

        await WithDb(async db =>
        {
            var queue = new JobQueueService(db);
            await queue.Enqueue("sweep_overdue_tasks", new { }, workspaceId: null);
        });
        await ProcessAllPendingJobsAsync();

        var notifications = await ws.Client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications");
        Assert.DoesNotContain(notifications!, n => n.Type == "task_overdue" && n.EntityId == futureTask.Id);
    }

    [Fact]
    public async Task SweepOverdueTasks_DoesNotNotifyForCompletedTasks()
    {
        var ws = await SeedWorkspaceAsync();
        var completedTask = TestData.TaskItem(ws.Workspace, "Done already");
        completedTask.AssignedToUserId = ws.User.Id;
        completedTask.DueAt = DateTime.UtcNow.AddDays(-1);
        completedTask.CompletedAt = DateTime.UtcNow;
        await WithDb(async db => { db.Tasks.Add(completedTask); await db.SaveChangesAsync(); });

        await WithDb(async db =>
        {
            var queue = new JobQueueService(db);
            await queue.Enqueue("sweep_overdue_tasks", new { }, workspaceId: null);
        });
        await ProcessAllPendingJobsAsync();

        var notifications = await ws.Client.GetFromJsonAsync<List<NotificationDto>>("/api/notifications");
        Assert.DoesNotContain(notifications!, n => n.Type == "task_overdue" && n.EntityId == completedTask.Id);
    }

    [Fact]
    public async Task SweepOverdueTasks_DoesNotDoubleNotifyOnASecondRun()
    {
        var ws = await SeedWorkspaceAsync();
        var overdueTask = TestData.TaskItem(ws.Workspace, "Overdue thing");
        overdueTask.AssignedToUserId = ws.User.Id;
        overdueTask.DueAt = DateTime.UtcNow.AddDays(-1);
        await WithDb(async db => { db.Tasks.Add(overdueTask); await db.SaveChangesAsync(); });

        // Run the sweep twice — the second run must not create a duplicate
        // notification for the same still-overdue task.
        for (var i = 0; i < 2; i++)
        {
            await WithDb(async db =>
            {
                var queue = new JobQueueService(db);
                await queue.Enqueue("sweep_overdue_tasks", new { }, workspaceId: null);
            });
            await ProcessAllPendingJobsAsync();
        }

        var count = await WithDb(db => db.Notifications.CountAsync(
            n => n.Type == "task_overdue" && n.EntityId == overdueTask.Id));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SweepOverdueTasks_ReschedulesItsNextRun()
    {
        var ws = await SeedWorkspaceAsync();
        await WithDb(async db =>
        {
            var queue = new JobQueueService(db);
            await queue.Enqueue("sweep_overdue_tasks", new { }, workspaceId: null);
        });

        await ProcessAllPendingJobsAsync();

        // Scoped to the most recently created pending sweep, not .Single():
        // this shared test DB (see TestData's fresh-Guid-per-row convention)
        // may already carry pending "next run" jobs left behind by other
        // tests in this class that also drove a sweep to completion.
        var nextRun = await WithDb(db => db.Jobs
            .Where(j => j.Type == "sweep_overdue_tasks" && j.Status == "pending")
            .OrderByDescending(j => j.CreatedAt)
            .FirstAsync());
        Assert.True(nextRun.RunAt > DateTime.UtcNow.AddMinutes(4));
    }
}
