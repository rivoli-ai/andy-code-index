# Story 015: Insight Analysis Report — Ratings, Feedback, and Exportable Report

**Priority:** Medium
**Component:** Backend API, Frontend
**Labels:** feature, story

**Depends on #014 (Repository Insights Lenses).**

## Description

Build on top of the insight layers from #014 to provide actionable analysis: ratings per layer, constructive feedback, improvement recommendations, and a comprehensive exportable report (web page or PDF).

## Analysis Dimensions

For each insight layer, generate:

### Per-Layer Ratings (1-5 scale)
- **Maturity:** How complete/mature is this aspect?
- **Quality:** How well-implemented?
- **Risk:** What level of risk does the current state carry?
- **Improvement potential:** How much room for improvement?

### Per-Layer Feedback
- **Strengths:** What's done well (with specific examples from code)
- **Weaknesses:** What needs improvement (with specific references)
- **Recommendations:** Top 3 actionable improvements, prioritized by impact/effort

### Aggregate Scores
- **Overall Health Score:** Weighted average across all layers (0-100)
- **Perceived Velocity:** Based on commit frequency, feature delivery rate, test coverage trends
- **Code Quality Index:** Based on implementation analysis, test coverage, security posture
- **Top 5 Improvements:** Cross-layer prioritized list with estimated impact

## Report Content Structure

```
1. Executive Summary
   - Overall health score with trend
   - Key strengths (top 3)
   - Critical improvements (top 5)
   - Velocity assessment

2. Feature Analysis
   - Feature inventory with stable IDs
   - Feature complexity/importance matrix
   - Suggested feature improvements
   - Rating: maturity, quality

3. Architecture Analysis
   - Architecture diagram (Mermaid)
   - Pattern identification
   - Constructive criticism
   - Rating: maturity, quality, risk

4. Design Analysis
   - Domain model diagram
   - API surface review
   - Design pattern usage
   - Rating + recommendations

5. Implementation Analysis
   - Code quality hotspots
   - Cross-language patterns
   - Key improvements
   - Rating + recommendations

6. Dependencies
   - Dependency tree visualization
   - Outdated/vulnerable packages
   - License compatibility
   - Rating + recommendations

7. Testing & Quality
   - Test pyramid chart
   - Coverage gaps
   - Test quality assessment
   - Rating + recommendations

8. Security
   - Security checklist results
   - Risk ratings
   - Remediation priorities
   - Rating

9. Deployment & CI/CD
   - Pipeline analysis
   - Environment matrix
   - Improvement suggestions
   - Rating

10. Operations
    - Logging audit results
    - Monitoring gaps
    - Operational readiness
    - Rating

11. Local Development
    - Setup guide quality
    - Prerequisites completeness
    - Rating

12. Summary & Roadmap
    - Prioritized improvement roadmap
    - Effort/impact matrix
    - Recommended next steps
```

## Acceptance Criteria

### Backend
- [ ] New enrichment subtype: `InsightReport`
- [ ] Report generation handler that aggregates all insight layers
- [ ] Scoring algorithm: weighted ratings per layer → overall score
- [ ] Velocity calculation from commit history + feature delivery
- [ ] Top 5 improvements algorithm: cross-layer prioritization by impact × (1/effort)
- [ ] API endpoint: `GET /api/v1/repositories/{id}/report` — full report JSON
- [ ] API endpoint: `GET /api/v1/repositories/{id}/report/summary` — executive summary only
- [ ] Report cached and regenerated only when insights change

### Export
- [ ] `GET /api/v1/repositories/{id}/report?format=html` — single-page printable HTML
- [ ] `GET /api/v1/repositories/{id}/report?format=pdf` — PDF download (via headless browser or wkhtmltopdf)
- [ ] HTML report: self-contained (inline CSS, Mermaid rendered as SVG, no external deps)
- [ ] PDF report: proper pagination, table of contents, page numbers

### Frontend
- [ ] "Report" button on repository detail page
- [ ] Report viewer page with all sections
- [ ] Mermaid diagrams rendered inline
- [ ] Radar chart for per-layer ratings
- [ ] Health score gauge/badge
- [ ] Export buttons (HTML, PDF)
- [ ] Print-friendly CSS

### Multi-Repo Support (future extension)
- [ ] `GET /api/v1/report/portfolio` — aggregate report across multiple repos
- [ ] Compare repos side-by-side on each dimension
- [ ] Portfolio health score
- [ ] Cross-repo dependency analysis

### MCP Tools
- [ ] `code_index_report` — params: repo_url, format (json/summary) — returns report
- [ ] `code_index_health_score` — params: repo_url — returns score + top improvements

### CLI
- [ ] `report --repo <id>` — print summary to terminal
- [ ] `report --repo <id> --format html --output report.html` — export HTML
- [ ] `report --repo <id> --format pdf --output report.pdf` — export PDF

## Testing Plan

### Unit Tests
- Scoring algorithm produces correct weighted average
- Velocity calculation from commit data
- Top 5 improvements selection and ranking
- Report aggregation from insight layers
- HTML export produces valid self-contained HTML
- Rating scale validation (1-5)

### Integration Tests
- Full report generation for sample repo
- Report accessible via API
- HTML export renders correctly
- PDF export generates valid PDF
- Report caching works (same request returns cached)

## Documentation Plan
- `docs/design.md` — Report architecture, scoring algorithm
- `docs/implementation.md` — Report generation, export pipeline
- `README.md` — Repository insights and report feature
