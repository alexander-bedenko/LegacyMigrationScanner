using System.Globalization;
using System.Net;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Packages;

namespace LegacyMigrationScanner.Core.Reporting;

/// <summary>
/// Single-file HTML dashboard: inline CSS and a few lines of optional JS (filtering), no external requests,
/// so it can be attached to a CI run or e-mailed. Light/dark follows the viewer's system setting.
/// </summary>
public sealed class HtmlReportWriter : IReportWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string Format => "html";
    public string FileExtension => ".html";

    public void Write(ScanResult result, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(writer);
        var r = result;
        var w = writer;

        w.WriteLine("<!doctype html>");
        w.WriteLine("<html lang=\"en\">");
        w.WriteLine("<head>");
        w.WriteLine("<meta charset=\"utf-8\">");
        w.WriteLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        w.WriteLine($"<title>Migration report: {E(r.Solution)}</title>");
        w.WriteLine($"<style>{Css}</style>");
        w.WriteLine("</head>");
        w.WriteLine("<body>");
        w.WriteLine("<main>");

        w.WriteLine("<header>");
        w.WriteLine($"<h1>Migration report: {E(r.Solution)}</h1>");
        w.WriteLine(string.Create(Inv,
            $"<p class=\"muted\">Scanned {r.ScannedAt:yyyy-MM-dd HH:mm} with Legacy Migration Scanner {E(r.ToolVersion)} · estimate assumes {r.HoursPerPoint:0.##} h per point</p>"));
        w.WriteLine("</header>");

        WriteTiles(r, w);
        WriteWarnings(r, w);
        WriteBaseline(r, w);
        WriteProjects(r, w);
        WriteWaves(r, w);
        WriteRules(r, w);
        WriteFindings(r, w);
        WritePackages(r, w);

        w.WriteLine("</main>");
        w.WriteLine($"<script>{Script}</script>");
        w.WriteLine("</body>");
        w.WriteLine("</html>");
    }

    private static void WriteTiles(ScanResult r, TextWriter w)
    {
        int Count(Severity s) => r.Findings.Count(f => f.Severity == s);
        w.WriteLine("<section class=\"tiles\" aria-label=\"Summary\">");
        Tile(w, "Projects", r.Projects.Count.ToString(Inv), null);
        Tile(w, "Findings", r.Findings.Count.ToString(Inv),
            string.Create(Inv, $"<span class=\"sev critical\">{Count(Severity.Critical)} critical</span> <span class=\"sev warning\">{Count(Severity.Warning)} warning</span> <span class=\"sev info\">{Count(Severity.Info)} info</span>"));
        Tile(w, "Effort P50", string.Create(Inv, $"{r.TotalHoursP50:0} h"), string.Create(Inv, $"≈ {r.TotalHoursP50 / 8:0.#} person-days"));
        Tile(w, "Effort P90", string.Create(Inv, $"{r.TotalHoursP90:0} h"), string.Create(Inv, $"≈ {r.TotalHoursP90 / 8:0.#} person-days"));
        if (r.Baseline is { } b)
        {
            Tile(w, "Since baseline", string.Create(Inv, $"+{b.New.Count} / −{b.Fixed.Count}"), string.Create(Inv, $"{b.Unchanged} unchanged"));
        }

        w.WriteLine("</section>");
    }

    private static void Tile(TextWriter w, string label, string value, string? noteHtml)
    {
        w.WriteLine($"<div class=\"tile\"><div class=\"label\">{E(label)}</div><div class=\"value\">{E(value)}</div>" +
                    (noteHtml is null ? "" : $"<div class=\"note\">{noteHtml}</div>") + "</div>");
    }

    private static void WriteWarnings(ScanResult r, TextWriter w)
    {
        if (r.Warnings.Count == 0)
        {
            return;
        }

        w.WriteLine("<section class=\"callout\"><strong>Scanner warnings</strong><ul>");
        foreach (var warning in r.Warnings)
        {
            w.WriteLine($"<li>{E(warning)}</li>");
        }

        w.WriteLine("</ul></section>");
    }

    private static void WriteBaseline(ScanResult r, TextWriter w)
    {
        if (r.Baseline is not { } b)
        {
            return;
        }

        w.WriteLine("<section><h2>Changes since baseline</h2>");
        var version = b.BaselineToolVersion is null ? "" : $", scanner {E(b.BaselineToolVersion)}";
        w.WriteLine(string.Create(Inv,
            $"<p>Compared with <code>{E(b.BaselinePath)}</code> ({b.BaselineFindingCount} findings{version}): <strong>{b.New.Count} new</strong>, {b.Fixed.Count} fixed, {b.Unchanged} unchanged.</p>"));
        if (b.New.Count > 0)
        {
            w.WriteLine("<h3>New findings</h3>");
            FindingTable(w, b.New.Select(f => (f.Severity, f.RuleId, f.Title, f.Project, Location(f.File, f.Line), f.Detail, (int?)null)), id: null);
        }

        if (b.Fixed.Count > 0)
        {
            w.WriteLine("<h3>Fixed since baseline</h3>");
            FindingTable(w, b.Fixed.Select(f => (f.Severity, f.RuleId, f.Title, f.Project, f.File, f.Detail, (int?)null)), id: null);
        }

        w.WriteLine("</section>");
    }

    private static void WriteProjects(ScanResult r, TextWriter w)
    {
        w.WriteLine("<section><h2>Projects</h2>");
        var max = r.Estimates.Select(e => e.HoursP90).DefaultIfEmpty(0).Max();
        w.WriteLine("<div class=\"scroll\"><table><thead><tr><th>Project</th><th>Kind</th><th>Format</th><th>Target</th>" +
                    "<th class=\"num\">LOC</th><th>Readiness</th><th class=\"num\">P50, h</th><th class=\"num\">P90, h</th>" +
                    "<th class=\"bar-col\">Effort</th><th>Top cost drivers</th></tr></thead><tbody>");
        foreach (var e in r.Estimates.OrderByDescending(e => e.HoursP50))
        {
            var p = r.Projects.FirstOrDefault(x => x.Name == e.Project);
            var drivers = string.Join(", ", e.Breakdown.Take(3).Select(b => string.Create(Inv, $"{b.RuleId} ({b.Points:0.#})")));
            var p50 = max > 0 ? e.HoursP50 / max * 100 : 0;
            var p90 = max > 0 ? e.HoursP90 / max * 100 : 0;
            w.WriteLine(string.Create(Inv,
                $"<tr><td>{E(e.Project)}</td><td>{e.Kind}</td><td>{(p is null ? "" : p.IsSdkStyle ? "SDK" : "Legacy")}</td>" +
                $"<td>{E(p is null ? "" : string.Join(", ", p.TargetFrameworks))}</td><td class=\"num\">{e.LinesOfCode}</td>" +
                $"<td><span class=\"badge {e.Readiness.ToLowerInvariant()}\">{E(e.Readiness)}</span></td>" +
                $"<td class=\"num\">{e.HoursP50:0}</td><td class=\"num\">{e.HoursP90:0}</td>" +
                $"<td class=\"bar-col\"><div class=\"bar\" title=\"P50 {e.HoursP50:0} h, P90 {e.HoursP90:0} h\">" +
                $"<span class=\"p90\" style=\"width:{p90:0.#}%\"></span><span class=\"p50\" style=\"width:{p50:0.#}%\"></span></div></td>" +
                $"<td>{E(drivers)}</td></tr>"));
        }

        w.WriteLine("</tbody></table></div>");
        var clean = r.Estimates.Where(e => e.Breakdown.Count == 0).Select(e => e.Project).ToList();
        if (clean.Count > 0)
        {
            w.WriteLine($"<p class=\"muted\">No issues found in: {E(string.Join(", ", clean))}.</p>");
        }

        w.WriteLine("</section>");
    }

    private static void WriteWaves(ScanResult r, TextWriter w)
    {
        w.WriteLine("<section><h2>Migration order</h2>");
        w.WriteLine("<p class=\"muted\">Projects in the same wave do not depend on each other and can be migrated in parallel.</p>");
        w.WriteLine("<ol class=\"waves\">");
        foreach (var wave in r.MigrationOrder)
        {
            w.WriteLine("<li>" + string.Concat(wave.Select(p => $"<span class=\"chip\">{E(p)}</span>")) + "</li>");
        }

        w.WriteLine("</ol></section>");
    }

    private static void WriteRules(ScanResult r, TextWriter w)
    {
        w.WriteLine("<section><h2>Issues by rule</h2>");
        w.WriteLine("<div class=\"scroll\"><table><thead><tr><th>Rule</th><th>Severity</th><th>Title</th>" +
                    "<th class=\"num\">Projects</th><th class=\"num\">Occurrences</th><th>Recommendation</th></tr></thead><tbody>");
        foreach (var g in r.Findings.GroupBy(f => f.RuleId).OrderByDescending(g => g.Max(f => f.Severity)).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var first = g.First();
            var recommendation = first.Category == FindingCategory.Package ? "See packages below" : first.Recommendation;
            w.WriteLine(string.Create(Inv,
                $"<tr><td><code>{E(g.Key)}</code></td><td>{Sev(first.Severity)}</td><td>{E(first.Title)}</td>" +
                $"<td class=\"num\">{g.Select(f => f.Project).Distinct().Count()}</td><td class=\"num\">{g.Sum(f => f.Count)}</td>" +
                $"<td>{E(recommendation)}</td></tr>"));
        }

        w.WriteLine("</tbody></table></div></section>");
    }

    private static void WriteFindings(ScanResult r, TextWriter w)
    {
        w.WriteLine("<section><h2>Findings</h2>");
        w.WriteLine("<div class=\"filters\" hidden>" +
                    "<input type=\"search\" id=\"q\" placeholder=\"Filter by rule, project, file, detail…\" aria-label=\"Filter findings\">" +
                    "<label><input type=\"checkbox\" data-sev=\"critical\" checked> Critical</label>" +
                    "<label><input type=\"checkbox\" data-sev=\"warning\" checked> Warning</label>" +
                    "<label><input type=\"checkbox\" data-sev=\"info\" checked> Info</label>" +
                    "<span id=\"shown\" class=\"muted\"></span></div>");
        FindingTable(w, r.Findings.Select(f => (f.Severity, f.RuleId, f.Title, f.Project, Location(f.File, f.Line), f.Detail, (int?)f.Count)), id: "findings",
            showCount: true);
        w.WriteLine("</section>");
    }

    private static void FindingTable(
        TextWriter w,
        IEnumerable<(Severity Severity, string RuleId, string Title, string Project, string? Location, string? Detail, int? Count)> rows,
        string? id,
        bool showCount = false)
    {
        w.WriteLine((id is null ? "<div class=\"scroll\"><table>" : $"<div class=\"scroll\"><table id=\"{id}\">") +
                    "<thead><tr><th>Severity</th><th>Rule</th><th>Project</th><th>Location</th>" +
                    (showCount ? "<th class=\"num\">Count</th>" : "") + "<th>Detail</th></tr></thead><tbody>");
        foreach (var f in rows)
        {
            var count = showCount ? string.Create(Inv, $"<td class=\"num\">{f.Count}</td>") : "";
            w.WriteLine($"<tr data-sev=\"{f.Severity.ToString().ToLowerInvariant()}\"><td>{Sev(f.Severity)}</td>" +
                        $"<td><code>{E(f.RuleId)}</code> {E(f.Title)}</td><td>{E(f.Project)}</td>" +
                        $"<td>{(string.IsNullOrEmpty(f.Location) ? "" : $"<code>{E(f.Location)}</code>")}</td>{count}<td>{E(f.Detail)}</td></tr>");
        }

        w.WriteLine("</tbody></table></div>");
    }

    private static void WritePackages(ScanResult r, TextWriter w)
    {
        var interesting = r.Packages.Where(p => p.Status != PackageStatus.Compatible).ToList();
        w.WriteLine("<section><h2>Packages</h2>");
        w.WriteLine(string.Create(Inv,
            $"<p class=\"muted\">{r.Packages.Count} distinct package version(s), {r.Packages.Count - interesting.Count} compatible as-is.</p>"));
        if (interesting.Count > 0)
        {
            w.WriteLine("<div class=\"scroll\"><table><thead><tr><th>Package</th><th>Version</th><th>Status</th><th>Target</th><th>Source</th><th>Note</th></tr></thead><tbody>");
            foreach (var p in interesting.OrderBy(p => p.Status).ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
            {
                w.WriteLine($"<tr><td>{E(p.Id)}</td><td>{E(p.Version)}</td><td>{p.Status}</td>" +
                            $"<td>{E(p.RecommendedVersion ?? p.Replacement)}</td><td>{E(p.Origin)}</td><td>{E(p.Reason)}</td></tr>");
            }

            w.WriteLine("</tbody></table></div>");
        }

        w.WriteLine("</section>");
    }

    private static string? Location(string? file, int? line) =>
        file is null ? null : line is null ? file : string.Create(Inv, $"{file}:{line}");

    private static string Sev(Severity severity) =>
        $"<span class=\"sev {severity.ToString().ToLowerInvariant()}\">{severity}</span>";

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");

    private const string Css = """
        :root{--bg:#f7f7f5;--panel:#fff;--text:#1d1d1f;--muted:#6b6b70;--border:#e2e2e0;--accent:#3b5bdb;
        --critical:#c92a2a;--critical-bg:#fdecec;--warning:#a35f00;--warning-bg:#fff3e0;--info:#1c6e8c;--info-bg:#e6f4f8;
        --green:#2b8a3e;--green-bg:#e9f7ec;--bar:#c5cdf5;color-scheme:light dark}
        @media (prefers-color-scheme:dark){:root{--bg:#141416;--panel:#1d1d20;--text:#ececef;--muted:#9a9aa2;--border:#323238;
        --accent:#8ea2ff;--critical:#ff8787;--critical-bg:#3a1d1f;--warning:#ffc078;--warning-bg:#3a2a14;--info:#74c0fc;
        --info-bg:#15303d;--green:#8ce99a;--green-bg:#183222;--bar:#3b4580}}
        *{box-sizing:border-box}
        body{margin:0;background:var(--bg);color:var(--text);font:14px/1.5 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}
        main{max-width:1200px;margin:0 auto;padding:24px 16px 48px}
        h1{font-size:24px;margin:0 0 4px}h2{font-size:18px;margin:32px 0 12px}h3{font-size:15px;margin:20px 0 8px}
        .muted{color:var(--muted)}code{font:12px/1.4 ui-monospace,SFMono-Regular,Consolas,monospace}
        .tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:12px;margin-top:20px}
        .tile,.callout,table{background:var(--panel);border:1px solid var(--border);border-radius:10px}
        .tile{padding:14px 16px}.tile .label{color:var(--muted);font-size:12px;text-transform:uppercase;letter-spacing:.04em}
        .tile .value{font-size:26px;font-weight:600;margin:2px 0}.tile .note{font-size:12px;color:var(--muted)}
        .callout{padding:12px 16px;margin-top:20px;border-left:4px solid var(--warning)}.callout ul{margin:6px 0 0;padding-left:20px}
        .scroll{overflow-x:auto}
        table{border-collapse:separate;border-spacing:0;width:100%;overflow:hidden}
        th,td{padding:8px 10px;text-align:left;vertical-align:top;border-bottom:1px solid var(--border)}
        tr:last-child td{border-bottom:0}th{font-size:12px;color:var(--muted);font-weight:600;white-space:nowrap}
        .num{text-align:right;font-variant-numeric:tabular-nums}
        .sev,.badge{display:inline-block;padding:1px 8px;border-radius:999px;font-size:12px;font-weight:600;white-space:nowrap}
        .sev.critical,.badge.red{color:var(--critical);background:var(--critical-bg)}
        .sev.warning,.badge.yellow{color:var(--warning);background:var(--warning-bg)}
        .sev.info{color:var(--info);background:var(--info-bg)}.badge.green{color:var(--green);background:var(--green-bg)}
        .bar-col{min-width:140px}.bar{position:relative;height:10px;border-radius:5px;background:var(--border);margin-top:5px}
        .bar span{position:absolute;left:0;top:0;bottom:0;border-radius:5px}.bar .p90{background:var(--bar)}.bar .p50{background:var(--accent)}
        .waves{padding-left:22px}.waves li{margin:6px 0}
        .chip{display:inline-block;padding:2px 10px;margin:0 6px 4px 0;border:1px solid var(--border);border-radius:999px;background:var(--panel)}
        .filters{display:flex;flex-wrap:wrap;gap:12px;align-items:center;margin-bottom:10px}
        .filters input[type=search]{flex:1 1 260px;padding:7px 10px;border:1px solid var(--border);border-radius:8px;background:var(--panel);color:var(--text)}
        """;

    // Progressive enhancement: without JS the full table is shown and the filter bar stays hidden.
    private const string Script = """
        (function(){var t=document.getElementById('findings');if(!t)return;var bar=document.querySelector('.filters');bar.hidden=false;
        var q=document.getElementById('q'),shown=document.getElementById('shown'),rows=[].slice.call(t.tBodies[0].rows),
        boxes=[].slice.call(bar.querySelectorAll('input[type=checkbox]'));
        function apply(){var s=q.value.trim().toLowerCase(),on={},n=0;boxes.forEach(function(b){on[b.dataset.sev]=b.checked});
        rows.forEach(function(r){var v=on[r.dataset.sev]&&(!s||r.textContent.toLowerCase().indexOf(s)>=0);r.hidden=!v;if(v)n++});
        shown.textContent=n+' of '+rows.length+' shown'}
        q.addEventListener('input',apply);boxes.forEach(function(b){b.addEventListener('change',apply)});apply()})();
        """;
}
