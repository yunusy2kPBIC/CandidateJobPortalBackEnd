using System.Text.Json;
using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Infrastructure;

namespace CandidatePortal.Api.Services;

internal static class SharePointProvisioner
{
    private delegate Task<IReadOnlyList<SharePointSetupResource>> MigrationAction(
        GraphSharePointClient client,
        PortalOptions options,
        string siteId,
        CancellationToken cancellationToken);

    private sealed record MigrationDefinition(string Id, string Description, MigrationAction Apply);

    private static readonly MigrationDefinition[] SchemaMigrations =
    [
        new(
            "SP202609300001_InitialBaseline",
            "Create or repair the current Candidate Portal SharePoint lists, libraries, columns, and choices.",
            ApplyInitialSchema),
        new(
            "SP202610030001_RecruitmentHiredAt",
            "Add the recruitment request hiring timestamp used for accurate ordering and reporting.",
            ApplyRecruitmentHiredAt),
    ];

    public static async Task<SharePointSetupResponse> ProvisionAsync(
        GraphSharePointClient client,
        PortalOptions options,
        CancellationToken cancellationToken)
    {
        var siteId = await client.GetSiteIdAsync(cancellationToken);
        var migrationList = await EnsureList(
            client, siteId, options.SharePointMigrationsList, "genericList", cancellationToken);
        await EnsureColumns(client, siteId, migrationList.Id, MigrationHistoryColumns(), cancellationToken);
        client.ClearListCache();

        var recordedMigrations = (await client.ListItemsAsync(options.SharePointMigrationsList, cancellationToken))
            .Where(item => item.Fields.TryGetValue("Title", out var value) &&
                !string.IsNullOrWhiteSpace(Convert.ToString(value)))
            .GroupBy(item => Convert.ToString(item.Fields["Title"])!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.CreatedAt).First(),
                StringComparer.OrdinalIgnoreCase);
        var resources = new Dictionary<string, SharePointSetupResource>(StringComparer.OrdinalIgnoreCase)
        {
            [options.SharePointMigrationsList] = migrationList,
        };
        var results = new List<SharePointMigrationResult>();

        foreach (var migration in SchemaMigrations)
        {
            var migrationResources = await migration.Apply(client, options, siteId, cancellationToken);
            foreach (var resource in migrationResources)
            {
                resources[resource.DisplayName ?? resource.Name ?? resource.Id] = resource;
            }

            if (recordedMigrations.TryGetValue(migration.Id, out var existing))
            {
                results.Add(new SharePointMigrationResult(
                    migration.Id, migration.Description, "verified", MigrationAppliedAt(existing)));
                continue;
            }

            client.ClearListCache();
            var appliedAt = DateTime.UtcNow;
            var recorded = await client.CreateItemAsync(
                options.SharePointMigrationsList,
                new Dictionary<string, object?>
                {
                    ["Title"] = migration.Id,
                    ["ProductVersion"] = typeof(SharePointProvisioner).Assembly.GetName().Version?.ToString() ?? "unknown",
                    ["AppliedAt"] = appliedAt.ToString("O"),
                    ["Description"] = migration.Description,
                },
                cancellationToken);
            results.Add(new SharePointMigrationResult(
                migration.Id, migration.Description, "applied", MigrationAppliedAt(recorded) ?? appliedAt));
        }

        client.ClearListCache();
        return new SharePointSetupResponse(
            siteId,
            string.IsNullOrWhiteSpace(options.SharePointSiteUrl) ? null : options.SharePointSiteUrl,
            SchemaMigrations[^1].Id,
            resources.Values.ToArray(),
            results);
    }

    private static async Task<IReadOnlyList<SharePointSetupResource>> ApplyInitialSchema(
        GraphSharePointClient client,
        PortalOptions options,
        string siteId,
        CancellationToken cancellationToken)
    {
        var resources = new List<SharePointSetupResource>();
        var candidates = await EnsureList(client, siteId, options.SharePointCandidatesList, "genericList", cancellationToken);
        resources.Add(candidates);
        var jobs = await EnsureList(client, siteId, options.SharePointJobsList, "genericList", cancellationToken);
        resources.Add(jobs);
        resources.Add(await EnsureList(client, siteId, options.SharePointApplicationsList, "genericList", cancellationToken));
        resources.Add(await EnsureList(client, siteId, options.SharePointResumesLibrary, "documentLibrary", cancellationToken));
        resources.Add(await EnsureList(client, siteId, options.SharePointRecruitmentRequestsList, "genericList", cancellationToken));
        resources.Add(await EnsureList(client, siteId, options.SharePointCooperativeTrainingList, "genericList", cancellationToken));
        resources.Add(await EnsureList(client, siteId, options.SharePointCooperativeTrainingDocumentsLibrary, "documentLibrary", cancellationToken));

        // Columns are deliberately repaired after all resources exist so lookup targets are available.
        await EnsureColumns(client, siteId, candidates.Id, CandidateColumns(), cancellationToken);
        await EnsureColumns(client, siteId, jobs.Id, JobColumns(), cancellationToken);
        await EnsureColumns(client, siteId,
            resources.Single(value => value.DisplayName == options.SharePointApplicationsList).Id,
            ApplicationColumns(candidates.Id, jobs.Id), cancellationToken);
        await EnsureColumns(client, siteId,
            resources.Single(value => value.DisplayName == options.SharePointResumesLibrary).Id,
            ResumeColumns(candidates.Id), cancellationToken);
        await EnsureColumns(client, siteId,
            resources.Single(value => value.DisplayName == options.SharePointRecruitmentRequestsList).Id,
            RecruitmentColumns(), cancellationToken);
        var training = resources.Single(value => value.DisplayName == options.SharePointCooperativeTrainingList);
        await EnsureColumns(client, siteId, training.Id, TrainingColumns(), cancellationToken);
        await EnsureColumns(client, siteId,
            resources.Single(value => value.DisplayName == options.SharePointCooperativeTrainingDocumentsLibrary).Id,
            TrainingDocumentColumns(training.Id), cancellationToken);
        return resources;
    }

    private static DateTime? MigrationAppliedAt(SharePointItemResponse item)
    {
        if (item.Fields.TryGetValue("AppliedAt", out var value) &&
            DateTime.TryParse(Convert.ToString(value), out var parsed))
        {
            return parsed;
        }
        return item.CreatedAt;
    }

    private static async Task<IReadOnlyList<SharePointSetupResource>> ApplyRecruitmentHiredAt(
        GraphSharePointClient client,
        PortalOptions options,
        string siteId,
        CancellationToken cancellationToken)
    {
        var recruitmentRequests = await EnsureList(
            client, siteId, options.SharePointRecruitmentRequestsList, "genericList", cancellationToken);
        await EnsureColumns(client, siteId, recruitmentRequests.Id,
            [Date("HiredAt", displayName: "Hired At")], cancellationToken);
        return [recruitmentRequests];
    }

    private static async Task<SharePointSetupResource> EnsureList(
        GraphSharePointClient client, string siteId, string displayName, string template, CancellationToken cancellationToken)
    {
        var existing = (await client.ListListsAsync(cancellationToken)).FirstOrDefault(value =>
            string.Equals(value.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (!string.Equals(existing.Template, template, StringComparison.OrdinalIgnoreCase))
                throw new ApiException(503, $"SharePoint resource '{displayName}' uses template '{existing.Template}', but '{template}' is required");
            return new SharePointSetupResource(existing.Id, existing.DisplayName, existing.Name, existing.WebUrl, existing.Template, "existing");
        }

        using var body = await client.SendAsync(HttpMethod.Post,
            $"/sites/{Uri.EscapeDataString(siteId)}/lists",
            new { displayName, list = new { template } }, null, cancellationToken);
        var root = body?.RootElement ?? throw new ApiException(502, $"Microsoft Graph did not return an ID for '{displayName}'");
        client.ClearListCache();
        return new SharePointSetupResource(
            root.GetProperty("id").GetString()!,
            root.TryGetProperty("displayName", out var shown) ? shown.GetString() : displayName,
            root.TryGetProperty("name", out var name) ? name.GetString() : null,
            root.TryGetProperty("webUrl", out var url) ? url.GetString() : null,
            template,
            "created");
    }

    private static async Task EnsureColumns(
        GraphSharePointClient client, string siteId, string listId,
        IReadOnlyList<Dictionary<string, object?>> definitions, CancellationToken cancellationToken)
    {
        using var body = await client.SendAsync(HttpMethod.Get,
            $"/sites/{Uri.EscapeDataString(siteId)}/lists/{Uri.EscapeDataString(listId)}/columns",
            null, null, cancellationToken);
        var existingColumns = body?.RootElement.GetProperty("value").EnumerateArray()
            .Select(value => value.Clone())
            .ToArray() ?? [];
        var existing = existingColumns
            .SelectMany(value => new[]
            {
                value.TryGetProperty("name", out var name) ? name.GetString() : null,
                value.TryGetProperty("displayName", out var display) ? display.GetString() : null,
            })
            .Where(value => value is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        foreach (var definition in definitions)
        {
            var name = Convert.ToString(definition["name"])!;
            var displayName = Convert.ToString(definition["displayName"])!;
            if (existing.Contains(name) || existing.Contains(displayName))
            {
                var column = existingColumns.First(value =>
                    (value.TryGetProperty("name", out var columnName) &&
                        string.Equals(columnName.GetString(), name, StringComparison.OrdinalIgnoreCase)) ||
                    (value.TryGetProperty("displayName", out var columnDisplayName) &&
                        string.Equals(columnDisplayName.GetString(), displayName, StringComparison.OrdinalIgnoreCase)));
                ValidateColumnType(column, definition, displayName);
                await AddMissingChoiceValues(client, siteId, listId, column, definition, cancellationToken);
                await SyncRequiredSetting(client, siteId, listId, column, definition, cancellationToken);
                continue;
            }
            using var _ = await client.SendAsync(HttpMethod.Post,
                $"/sites/{Uri.EscapeDataString(siteId)}/lists/{Uri.EscapeDataString(listId)}/columns",
                definition, null, cancellationToken);
            existing.Add(name);
            existing.Add(displayName);
        }
    }

    private static void ValidateColumnType(
        JsonElement column,
        IReadOnlyDictionary<string, object?> definition,
        string displayName)
    {
        var expectedType = new[] { "text", "choice", "boolean", "dateTime", "number", "lookup", "hyperlinkOrPicture" }
            .FirstOrDefault(definition.ContainsKey);
        if (expectedType is null || column.TryGetProperty(expectedType, out _))
        {
            return;
        }

        var internalName = column.TryGetProperty("name", out var name) ? name.GetString() : null;
        throw new ApiException(503,
            $"SharePoint column '{displayName}'{(internalName is null ? "" : $" (internal name '{internalName}')")} " +
            $"has a different type; expected '{expectedType}'. Create a compatible column before continuing.");
    }

    private static async Task AddMissingChoiceValues(
        GraphSharePointClient client,
        string siteId,
        string listId,
        JsonElement column,
        IReadOnlyDictionary<string, object?> definition,
        CancellationToken cancellationToken)
    {
        if (!definition.TryGetValue("choice", out var desiredChoiceValue) || desiredChoiceValue is null ||
            !column.TryGetProperty("choice", out var existingChoice) ||
            !existingChoice.TryGetProperty("choices", out var existingChoices))
            return;

        var desiredChoice = JsonSerializer.SerializeToElement(desiredChoiceValue);
        var merged = existingChoices.EnumerateArray()
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToList();
        foreach (var choice in desiredChoice.GetProperty("choices").EnumerateArray()
                     .Select(value => value.GetString())
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Cast<string>())
        {
            if (!merged.Contains(choice, StringComparer.OrdinalIgnoreCase)) merged.Add(choice);
        }
        if (merged.Count == existingChoices.GetArrayLength()) return;

        var columnId = column.GetProperty("id").GetString()
            ?? throw new ApiException(502, "SharePoint returned a column without an ID");
        var allowTextEntry = existingChoice.TryGetProperty("allowTextEntry", out var allowText) && allowText.GetBoolean();
        var displayAs = existingChoice.TryGetProperty("displayAs", out var display)
            ? display.GetString() ?? "dropDownMenu"
            : "dropDownMenu";
        using var _ = await client.SendAsync(HttpMethod.Patch,
            $"/sites/{Uri.EscapeDataString(siteId)}/lists/{Uri.EscapeDataString(listId)}/columns/{Uri.EscapeDataString(columnId)}",
            new { choice = new { allowTextEntry, choices = merged, displayAs } }, null, cancellationToken);
    }

    private static async Task SyncRequiredSetting(
        GraphSharePointClient client,
        string siteId,
        string listId,
        JsonElement column,
        IReadOnlyDictionary<string, object?> definition,
        CancellationToken cancellationToken)
    {
        if (!definition.TryGetValue("required", out var desiredValue) || desiredValue is not bool desired ||
            !column.TryGetProperty("required", out var currentValue) || currentValue.GetBoolean() == desired)
            return;

        var columnId = column.GetProperty("id").GetString()
            ?? throw new ApiException(502, "SharePoint returned a column without an ID");
        using var _ = await client.SendAsync(HttpMethod.Patch,
            $"/sites/{Uri.EscapeDataString(siteId)}/lists/{Uri.EscapeDataString(listId)}/columns/{Uri.EscapeDataString(columnId)}",
            new { required = desired }, null, cancellationToken);
    }

    private static Dictionary<string, object?> Text(string name, bool required = false, bool multiline = false, string? displayName = null) => new()
    {
        ["name"] = name,
        ["displayName"] = displayName ?? name,
        ["required"] = required,
        ["text"] = new { allowMultipleLines = multiline },
    };
    private static Dictionary<string, object?> Choice(string name, string[] choices, bool required = false, bool allowText = false, string? displayName = null) => new()
    {
        ["name"] = name,
        ["displayName"] = displayName ?? name,
        ["required"] = required,
        ["choice"] = new { allowTextEntry = allowText, choices, displayAs = "dropDownMenu" },
    };
    private static Dictionary<string, object?> Boolean(string name, string? displayName = null) => new()
    {
        ["name"] = name,
        ["displayName"] = displayName ?? name,
        ["boolean"] = new { },
    };
    private static Dictionary<string, object?> Date(string name, bool required = false, bool dateOnly = false, string? displayName = null) => new()
    {
        ["name"] = name,
        ["displayName"] = displayName ?? name,
        ["required"] = required,
        ["dateTime"] = new { displayAs = "default", format = dateOnly ? "dateOnly" : "dateTime" },
    };
    private static Dictionary<string, object?> Number(string name, bool required, double min, double max, string displayName) => new()
    {
        ["name"] = name,
        ["displayName"] = displayName,
        ["required"] = required,
        ["number"] = new { decimalPlaces = "none", displayAs = "number", minimum = min, maximum = max },
    };
    private static Dictionary<string, object?> Lookup(string name, string listId) => new()
    {
        ["name"] = name,
        ["displayName"] = name,
        ["required"] = true,
        ["lookup"] = new { allowMultipleValues = false, allowUnlimitedLength = false, columnName = "Title", listId },
    };

    private static IReadOnlyList<Dictionary<string, object?>> MigrationHistoryColumns() =>
    [
        Text("ProductVersion"),
        Date("AppliedAt"),
        Text("Description", multiline: true),
    ];

    private static IReadOnlyList<Dictionary<string, object?>> CandidateColumns() =>
    [
        Text("PortalCandidateId", true), Text("Email", true), Text("FirstName", true), Text("LastName", true),
        Text("CountryCode"), Text("Phone"), Text("Country"), Text("Nationality"),
        Choice("Gender", ["Male", "Female", "Other"]), Text("City"), Text("ProfessionalTitle"), Text("About", multiline: true),
        Choice("Role", ["Candidate", "Student", "HR Admin", "Admin"], true),
        new() { ["name"] = "ResumeUrl", ["displayName"] = "ResumeUrl", ["hyperlinkOrPicture"] = new { isPicture = false } },
    ];
    private static IReadOnlyList<Dictionary<string, object?>> JobColumns() =>
    [
        Text("PortalJobId", true), Text("Division", true), Text("Country", true), Text("City", true), Text("JobFunction", true),
        Choice("CareerLevel", ["Entry level", "Mid-level", "Senior"], true),
        Choice("EmploymentType", ["Full-time", "Part-time", "Contract", "Remote"], true),
        Text("Summary", true, true), Text("Description", true, true), Text("Requirements", true, true),
        Boolean("IsOpen"), Boolean("IsFeatured"), Date("PostedAt", true), Date("ExpiresAt", dateOnly: true),
    ];
    private static IReadOnlyList<Dictionary<string, object?>> ApplicationColumns(string candidates, string jobs) =>
    [
        Text("PortalApplicationId", true), Lookup("Candidate", candidates), Lookup("Job", jobs), Text("CandidateJobKey", true),
        Choice("Status", ["Under Review", "Interview", "Shortlisted", "Rejected", "Hired", "Withdrawn"], true), Date("AppliedAt", true), Date("HiredAt"),
    ];
    private static IReadOnlyList<Dictionary<string, object?>> ResumeColumns(string candidates) =>
        [Lookup("Candidate", candidates), Text("CandidateEmail", true), Date("UploadedAt", true), Boolean("IsCurrent")];
    private static IReadOnlyList<Dictionary<string, object?>> RecruitmentColumns() =>
    [
        Choice("PreferredPosition", ["Applications Project Manager", "Business Analyst", "System Administrator", "Software Developer", "IT Support Engineer"], true, true, "Preferred Position"),
        Text("Nationality", true),
        Choice("Gender", ["Male", "Female", "Other"], true),
        Choice("DriverLicenseType", ["Saudi License", "Valid GCC License", "Other License", "None"], true, displayName: "Driver License Type"),
        Text("MobileNumber", true, displayName: "Mobile Number"), Text("EmailAddress", true, displayName: "Email Address"),
        Text("IqamaNumber", true, displayName: "ID/Iqama Number"), Text("IqamaProfession", displayName: "Iqama Profession"),
        Text("CurrentEmployer", displayName: "Current Employer"), Date("DateOfBirth", true, true, "Date of Birth"),
        Text("City", true), Boolean("AcceptWorkInAnotherCity", "Accept Work in Another City"),
        Choice("Qualification", ["High School", "Diploma", "Bachelor's Degree", "Master's Degree", "Doctorate", "Other"], true),
        Number("CurrentSalary", true, 0, 100000000, "Current Salary (SAR)"), Text("Comments", multiline: true), Boolean("Hired"), Boolean("IsDeleted", "Is Deleted"),
    ];
    private static IReadOnlyList<Dictionary<string, object?>> TrainingColumns() =>
    [
        Text("FirstName", true, displayName: "First Name"), Text("LastName", true, displayName: "Last Name"),
        Text("IdNumber", true, displayName: "ID Number"), Text("MobileNumber", true, displayName: "Mobile Number"), Text("Email", true),
        Choice("Gender", ["Male", "Female", "Other"], true), Number("TrainingDuration", true, 3, 6, "Training Duration (Months)"),
        Choice("Semester", ["First Semester", "Second Semester", "Summer Semester"], true),
        Date("TrainingStartingDate", true, true, "Training Starting Date"),
        Text("TrainingSupervisorName", true, displayName: "Training Supervisor Name"),
        Text("TrainingSupervisorNumber", true, displayName: "Training Supervisor Number"),
        Text("TrainingSupervisorEmail", true, displayName: "Training Supervisor Email"),
        Text("UniversityCollege", true, displayName: "University / College"),
        Choice("Qualification", ["High School", "Diploma", "Bachelor's Degree", "Master's Degree", "Doctorate", "Other"], true),
        Text("Major", true), Choice("GpaScale", ["4", "5"], true, displayName: "GPA Scale"),
        Number("CumulativeGpa", true, 0, 5, "Cumulative GPA"), Choice("EnglishLevel", ["Beginner", "Intermediate", "Advanced", "Fluent"], true, displayName: "English Level"),
        Text("DesiredCityForTraining", true, displayName: "Desired City for Training"), Text("CurrentCityOfResidency", true, displayName: "Current City of Residency"),
        Boolean("Disability"), Boolean("DeclarationAccepted", "Declaration Accepted"),
        Choice("TrainingStatus", ["Under Training", "Completed"], displayName: "Training Status"), Date("CompletionDate", dateOnly: true, displayName: "Completion Date"),
        Boolean("IsDeleted", "Is Deleted"), Text("TranscriptUrl", displayName: "Transcript URL"),
        Text("TranscriptFileName", displayName: "Transcript File Name"), Text("UniversityRequestUrl", displayName: "University Request URL"),
        Text("UniversityRequestFileName", displayName: "University Request File Name"),
    ];
    private static IReadOnlyList<Dictionary<string, object?>> TrainingDocumentColumns(string training) =>
        [Lookup("TrainingRequest", training), Choice("DocumentType", ["Transcript", "University Request"], true), Text("ApplicantEmail", true), Date("UploadedAt", true), Boolean("IsCurrent")];
}
