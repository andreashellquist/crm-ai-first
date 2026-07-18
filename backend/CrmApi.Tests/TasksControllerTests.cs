using System.Net;
using System.Net.Http.Json;
using CrmApi.Dtos;
using Microsoft.EntityFrameworkCore;

namespace CrmApi.Tests;

[Collection(CrmApiCollection.Name)]
public class TasksControllerTests(CrmApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Create_Valid_Persists()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var response = await ws.Client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Follow up", DateTime.UtcNow.AddDays(2), null, null, deal.Id));

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<TaskDto>();
        Assert.NotNull(dto);
        Assert.Equal("Follow up", dto!.Title);
        Assert.Equal(deal.Id, dto.DealId);
        Assert.Null(dto.CompletedAt);
        Assert.False(dto.AiSuggested);
    }

    [Fact]
    public async Task Create_WithoutTitle_ReturnsBadRequest()
    {
        var ws = await SeedWorkspaceAsync();

        var response = await ws.Client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("", null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAnotherWorkspacesDeal_ReturnsBadRequest()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        var deal = TestData.Deal(owner.Workspace, owner.Pipeline, owner.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });

        var response = await intruder.Client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Snoop", null, null, null, deal.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_ExcludesCompletedByDefault()
    {
        var ws = await SeedWorkspaceAsync();
        var open = await ws.Client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Open task", null, null, null, null));
        var openDto = await open.Content.ReadFromJsonAsync<TaskDto>();
        var toComplete = await ws.Client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Done task", null, null, null, null));
        var toCompleteDto = await toComplete.Content.ReadFromJsonAsync<TaskDto>();
        await ws.Client.PutAsJsonAsync($"/api/tasks/{toCompleteDto!.Id}", new UpdateTaskRequest("Done task", null, true));

        var tasks = await ws.Client.GetFromJsonAsync<List<TaskDto>>("/api/tasks");

        Assert.NotNull(tasks);
        Assert.Contains(tasks!, t => t.Id == openDto!.Id);
        Assert.DoesNotContain(tasks, t => t.Id == toCompleteDto.Id);

        var withCompleted = await ws.Client.GetFromJsonAsync<List<TaskDto>>("/api/tasks?includeCompleted=true");
        Assert.Contains(withCompleted!, t => t.Id == toCompleteDto.Id);
    }

    [Fact]
    public async Task List_FiltersByDealId()
    {
        var ws = await SeedWorkspaceAsync();
        var deal = TestData.Deal(ws.Workspace, ws.Pipeline, ws.StageOne);
        await WithDb(async db => { db.Deals.Add(deal); await db.SaveChangesAsync(); });
        await ws.Client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Deal task", null, null, null, deal.Id));
        await ws.Client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Unrelated task", null, null, null, null));

        var tasks = await ws.Client.GetFromJsonAsync<List<TaskDto>>($"/api/tasks?dealId={deal.Id}");

        Assert.NotNull(tasks);
        Assert.Single(tasks!);
        Assert.Equal("Deal task", tasks![0].Title);
    }

    [Fact]
    public async Task Update_MarkComplete_SetsCompletedAt()
    {
        var ws = await SeedWorkspaceAsync();
        var created = await ws.Client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Task", null, null, null, null));
        var dto = await created.Content.ReadFromJsonAsync<TaskDto>();

        var response = await ws.Client.PutAsJsonAsync($"/api/tasks/{dto!.Id}", new UpdateTaskRequest("Task", null, true));

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<TaskDto>();
        Assert.NotNull(updated!.CompletedAt);

        var uncompleted = await ws.Client.PutAsJsonAsync($"/api/tasks/{dto.Id}", new UpdateTaskRequest("Task", null, false));
        var uncompletedDto = await uncompleted.Content.ReadFromJsonAsync<TaskDto>();
        Assert.Null(uncompletedDto!.CompletedAt);
    }

    [Fact]
    public async Task Update_ForAnotherWorkspacesTask_ReturnsNotFound()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        var created = await owner.Client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Task", null, null, null, null));
        var dto = await created.Content.ReadFromJsonAsync<TaskDto>();

        var response = await intruder.Client.PutAsJsonAsync($"/api/tasks/{dto!.Id}", new UpdateTaskRequest("Hijacked", null, true));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ForAnotherWorkspacesTask_ReturnsNotFound()
    {
        var owner = await SeedWorkspaceAsync();
        var intruder = await SeedWorkspaceAsync();
        var created = await owner.Client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Task", null, null, null, null));
        var dto = await created.Content.ReadFromJsonAsync<TaskDto>();

        var response = await intruder.Client.DeleteAsync($"/api/tasks/{dto!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var stillExists = await WithDb(db => db.Tasks.AnyAsync(t => t.Id == dto.Id));
        Assert.True(stillExists);
    }

    [Fact]
    public async Task Delete_AsOwner_Removes()
    {
        var ws = await SeedWorkspaceAsync();
        var created = await ws.Client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Task", null, null, null, null));
        var dto = await created.Content.ReadFromJsonAsync<TaskDto>();

        var response = await ws.Client.DeleteAsync($"/api/tasks/{dto!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
