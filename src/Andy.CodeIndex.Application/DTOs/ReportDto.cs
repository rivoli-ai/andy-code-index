namespace Andy.CodeIndex.Application.DTOs;

public class ReportDto
{
    public string RepositoryName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public int OverallHealthScore { get; set; }
    public VelocityDto Velocity { get; set; } = new();
    public List<LayerReportDto> Layers { get; set; } = [];
    public List<ImprovementDto> Top5Improvements { get; set; } = [];
}

public class VelocityDto
{
    public double CommitsPerMonth { get; set; }
    public int ActiveContributors { get; set; }
    public string Trend { get; set; } = "stable"; // increasing, stable, decreasing
    public List<ContributorDto> TopContributors { get; set; } = [];
}

public class ContributorDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Commits { get; set; }
}

public class LayerReportDto
{
    public string Name { get; set; } = string.Empty;
    public string Subtype { get; set; } = string.Empty;
    public int MaturityRating { get; set; }
    public int QualityRating { get; set; }
    public int RiskRating { get; set; }
    public List<string> Strengths { get; set; } = [];
    public List<string> Weaknesses { get; set; } = [];
    public List<string> Recommendations { get; set; } = [];
    public string Content { get; set; } = string.Empty;
    public bool HasMermaidDiagrams { get; set; }
}

public class ImprovementDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Layer { get; set; } = string.Empty;
    public string Impact { get; set; } = "medium"; // high, medium, low
    public string Effort { get; set; } = "medium"; // high, medium, low
}
