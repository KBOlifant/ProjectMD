namespace ProjectMD.Api.Dtos;

// One detected technology, as the frontend receives it: { name, highlighted }.
public record TechDto(string Name, bool Highlighted);
