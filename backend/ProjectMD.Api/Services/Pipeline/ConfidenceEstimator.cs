using ProjectMD.Api.Models;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Services.Pipeline;

// Works out the 0-100 "confidence" number shown on a scan.
//
// HEURISTIC, NOT A MEASUREMENT: it is a rough score for how much usable information the page gave
// us (a good response, a title, a description, headings, real text, structured data, recognised
// technologies). It says nothing about how accurate any statement in the document is, and the
// scan document labels it as an estimate. The weights are arbitrary but fixed and add up to 100.
public static class ConfidenceEstimator
{
    // Scores the page. Pure and deterministic: the same page always gets the same number.
    public static int Estimate(PageDigest digest, IReadOnlyList<ScanTech> tech)
    {
        var score = 0;

        // 30: the page answered with a success status (a 4xx/5xx page is barely documentation).
        if (digest.StatusCode is >= 200 and < 300)
        {
            score += 30;
        }
        else if (digest.StatusCode is >= 300 and < 400)
        {
            score += 15;
        }

        // 10 + 10: a title and a meta description.
        score += string.IsNullOrWhiteSpace(digest.Title) ? 0 : 10;
        score += string.IsNullOrWhiteSpace(digest.MetaDescription) ? 0 : 10;

        // 15: a heading outline (a little for one heading, the rest from three).
        score += digest.Outline.Count switch
        {
            >= 3 => 15,
            >= 1 => 8,
            _ => 0,
        };

        // 20: real readable text (a little from 100 characters, more from 500, full from 2000).
        score += digest.Text.Length switch
        {
            >= 2000 => 20,
            >= 500 => 14,
            >= 100 => 7,
            _ => 0,
        };

        // 5: structured data (JSON-LD) was present.
        score += digest.JsonLd.Count > 0 ? 5 : 0;

        // 10: recognised technologies (a little each, up to four).
        score += Math.Min(4, tech.Count) * 10 / 4;

        return Math.Clamp(score, 0, 100);
    }
}
