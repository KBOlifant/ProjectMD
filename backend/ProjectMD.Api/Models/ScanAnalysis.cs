using ProjectMD.Api.Services.Extraction;
using ProjectMD.Api.Services.Extraction.Components;

namespace ProjectMD.Api.Models;

// The structured data captured about a site, stored next to the Markdown (jsonb on the Scan row)
// and sent to the website as `analysis`. Version is 1 for now; later features add more members
// (components, animations) next to Design and Api, and readers ignore properties they do not know.
// Components holds the AI-written, sanitised HTML/CSS previews of the site's main components.
public sealed record ScanAnalysis(int Version, DesignSystem? Design, ApiCatalog? Api, ComponentPreviews? Components = null);
