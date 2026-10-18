using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Extraction.Components;
using ProjectMD.Api.Services.Pipeline;

namespace ProjectMD.Api.Models;

// The structured data captured about a site, stored next to the Markdown (jsonb on the Scan row)
// and sent to the website as `analysis`. Version is 1 for now; later features add more members
// (components, animations) next to Design and Api, and readers ignore properties they do not know.
// Components holds the AI-written, sanitised HTML/CSS previews of the site's main components.
// Animations holds the described animations with a library estimate and copyable GSAP / Framer Motion code.
public sealed record ScanAnalysis(
    int Version,
    DesignSystem? Design,
    ApiCatalog? Api,
    ComponentPreviews? Components = null,
    AnimationReport? Animations = null);
