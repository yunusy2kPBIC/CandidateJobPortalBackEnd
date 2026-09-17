using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Data;
using CandidatePortal.Api.Infrastructure;
using CandidatePortal.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace CandidatePortal.Api.Services;

public sealed class MasterDataService(PortalDbContext database)
{
    private static readonly IReadOnlyList<string> ResidenceCountries = CultureInfo
        .GetCultures(CultureTypes.SpecificCultures)
        .Select(culture => new RegionInfo(culture.Name).EnglishName)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(country => country, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static readonly HashSet<string> ResidenceCountryNames =
        new(ResidenceCountries, StringComparer.OrdinalIgnoreCase);

    public async Task<LookupOptionsResponse> GetOptionsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await database.LookupValues.AsNoTracking()
            .Where(value => value.IsActive)
            .OrderBy(value => value.SortOrder)
            .ThenBy(value => value.Value)
            .ToListAsync(cancellationToken);

        var cities = rows
            .Where(value => value.Category == LookupCategories.City && !string.IsNullOrWhiteSpace(value.ParentValue))
            .GroupBy(value => value.ParentValue!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(value => value.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var countries = rows
            .Where(value => value.Category == LookupCategories.Country)
            .Select(value => new LookupCountryResponse(
                value.Value,
                cities.TryGetValue(value.Value, out var countryCities) ? countryCities : []))
            .ToArray();

        return new LookupOptionsResponse(
            countries,
            ResidenceCountries,
            Values(rows, LookupCategories.Nationality),
            Values(rows, LookupCategories.Division),
            Values(rows, LookupCategories.JobFunction),
            Values(rows, LookupCategories.CareerLevel));
    }

    public async Task ValidateResidenceCountryAsync(
        string country,
        CancellationToken cancellationToken = default)
    {
        var normalized = country.Trim();
        if (ResidenceCountryNames.Contains(normalized) ||
            await IsActiveAsync(LookupCategories.Country, normalized, null, cancellationToken))
            return;

        throw new ApiException(400, "Select a valid country");
    }

    public async Task ValidateCountryAsync(string country, CancellationToken cancellationToken = default)
    {
        if (!await IsActiveAsync(LookupCategories.Country, country, null, cancellationToken))
            throw new ApiException(400, "Select a valid country");
    }

    public async Task ValidateNationalityAsync(string nationality, CancellationToken cancellationToken = default)
    {
        if (!await IsActiveAsync(LookupCategories.Nationality, nationality, null, cancellationToken))
            throw new ApiException(400, "Select a valid nationality");
    }

    public async Task ValidateCountryCityAsync(string country, string city, CancellationToken cancellationToken = default)
    {
        await ValidateCountryAsync(country, cancellationToken);
        if (!await IsActiveAsync(LookupCategories.City, city, country, cancellationToken))
            throw new ApiException(400, "Select a city that belongs to the selected country");
    }

    public async Task ValidateJobAsync(
        string country,
        string city,
        string division,
        string jobFunction,
        string careerLevel,
        CancellationToken cancellationToken = default)
    {
        await ValidateCountryCityAsync(country, city, cancellationToken);
        if (!await IsActiveAsync(LookupCategories.Division, division, null, cancellationToken))
            throw new ApiException(400, "Select a valid division");
        if (!await IsActiveAsync(LookupCategories.JobFunction, jobFunction, null, cancellationToken))
            throw new ApiException(400, "Select a valid job function");
        if (!await IsActiveAsync(LookupCategories.CareerLevel, careerLevel, null, cancellationToken))
            throw new ApiException(400, "Select a valid career level");
    }

    private Task<bool> IsActiveAsync(
        string category,
        string value,
        string? parentValue,
        CancellationToken cancellationToken) =>
        database.LookupValues.AsNoTracking().AnyAsync(item =>
            item.Category == category &&
            item.Value == value.Trim() &&
            item.ParentValue == parentValue &&
            item.IsActive,
            cancellationToken);

    private static IReadOnlyList<string> Values(IEnumerable<LookupValue> rows, string category) =>
        rows.Where(value => value.Category == category)
            .Select(value => value.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
