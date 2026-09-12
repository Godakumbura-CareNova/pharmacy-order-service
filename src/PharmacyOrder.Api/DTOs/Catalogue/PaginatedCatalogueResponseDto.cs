namespace PharmacyOrder.Api.DTOs.Catalogue;

public class PaginatedCatalogueResponseDto
{
    public IReadOnlyList<CatalogueMedicineResponseDto> Items { get; init; } = Array.Empty<CatalogueMedicineResponseDto>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}
