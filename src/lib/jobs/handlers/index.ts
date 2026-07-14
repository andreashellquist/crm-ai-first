// Importing each handler module registers it (side effect). Add new job
// handlers here so both the worker entrypoint and any future cron-triggered
// endpoint load the same registry.
import "./score-deal";
