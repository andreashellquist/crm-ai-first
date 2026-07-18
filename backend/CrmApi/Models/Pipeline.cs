namespace CrmApi.Models;

public class Pipeline
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string WorkspaceId { get; set; }
    public required string Name { get; set; }
    public bool IsDefault { get; set; }

    public Workspace? Workspace { get; set; }
    public List<Stage> Stages { get; set; } = [];
    public List<Deal> Deals { get; set; } = [];
}

public class Stage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string PipelineId { get; set; }
    public required string Name { get; set; }
    public int Order { get; set; }
    public int Probability { get; set; }
    public bool IsWon { get; set; }
    public bool IsLost { get; set; }

    public Pipeline? Pipeline { get; set; }
    public List<Deal> Deals { get; set; } = [];
}
