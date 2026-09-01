using System.Text.Json;
using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Contracts;
using CandidatePortal.Api.Infrastructure;

namespace CandidatePortal.Api.Services;

internal static class SharePointProvisioner
{
    public static async Task<SharePointSetupResponse> ProvisionAsync(
        GraphSharePointClient client,
        PortalOptions options,
        CancellationToken cancellationToken)
    {
        var siteId = await client.GetSiteIdAsync(cancellationToken);
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
        client.ClearListCache();
        return new SharePointSetupResponse(siteId, string.IsNullOrWhiteSpace(options.SharePointSiteUrl) ? null : options.SharePointSiteUrl, resources);
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
        var existing = body?.RootElement.GetProperty("value").EnumerateArray()
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
            if (existing.Contains(name) || existing.Contains(displayName)) continue;
            using var _ = await client.SendAsync(HttpMethod.Post,
                $"/sites/{Uri.EscapeDataString(siteId)}/lists/{Uri.EscapeDataString(listId)}/columns",
                definition, null, cancellationToken);
            existing.Add(name);
            existing.Add(displayName);
        }
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
        ["number"] = new { decimalPlaces = "automatic", displayAs = "number", minimum = min, maximum = max },
    };
    private static Dictionary<string, object?> Lookup(string name, string listId) => new()
    {
        ["name"] = name,
        ["displayName"] = name,
        ["required"] = true,
        ["lookup"] = new { allowMultipleValues = false, allowUnlimitedLength = false, columnName = "Title", listId },
    };

    private static IReadOnlyList<Dictionary<string, object?>> CandidateColumns() =>
    [
        Text("PortalCandidateId", true), Text("Email", true), Text("FirstName", true), Text("LastName", true),
        Text("CountryCode"), Text("Phone"), Text("Country"), Text("City"), Text("ProfessionalTitle"), Text("About", multiline: true),
        Choice("Role", ["Candidate", "Admin"], true),
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
        Choice("Status", ["Under Review", "Interview", "Shortlisted", "Rejected", "Hired", "Withdrawn"], true), Date("AppliedAt", true),
    ];
    private static IReadOnlyList<Dictionary<string, object?>> ResumeColumns(string candidates) =>
        [Lookup("Candidate", candidates), Text("CandidateEmail", true), Date("UploadedAt", true), Boolean("IsCurrent")];
    private static IReadOnlyList<Dictionary<string, object?>> RecruitmentColumns() =>
    [
        Choice("PreferredPosition", ["Applications Project Manager", "Business Analyst", "System Administrator", "Software Developer", "IT Support Engineer"], true, true, "Preferred Position"),
        Choice("Nationality", ["Saudi", "Indian", "Egyptian", "Pakistani", "Other"], true, true),
        Choice("Gender", ["Male", "Female", "Other"], true),
        Choice("DriverLicenseType", ["Saudi License", "Valid GCC License", "Other License", "None"], true, displayName: "Driver License Type"),
        Text("MobileNumber", true, displayName: "Mobile Number"), Text("EmailAddress", true, displayName: "Email Address"),
        Text("IqamaNumber", true, displayName: "ID/Iqama Number"), Text("IqamaProfession", true, displayName: "Iqama Profession"),
        Text("CurrentEmployer", displayName: "Current Employer"), Date("DateOfBirth", true, true, "Date of Birth"),
        Text("City", true), Boolean("AcceptWorkInAnotherCity", "Accept Work in Another City"),
        Choice("Qualification", ["High School", "Diploma", "Bachelor's Degree", "Master's Degree", "Doctorate", "Other"], true),
        Number("CurrentSalary", true, 0, 100000000, "Current Salary (SAR)"), Text("Comments", multiline: true),
    ];
    private static IReadOnlyList<Dictionary<string, object?>> TrainingColumns() =>
    [
        Text("FirstName", true, displayName: "First Name"), Text("LastName", true, displayName: "Last Name"),
        Text("IdNumber", true, displayName: "ID Number"), Text("MobileNumber", true, displayName: "Mobile Number"), Text("Email", true),
        Choice("Gender", ["Male", "Female", "Other"], true), Number("TrainingDuration", true, 1, 24, "Training Duration (Months)"),
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
        Boolean("Disability"), Boolean("DeclarationAccepted", "Declaration Accepted"), Text("TranscriptUrl", displayName: "Transcript URL"),
        Text("TranscriptFileName", displayName: "Transcript File Name"), Text("UniversityRequestUrl", displayName: "University Request URL"),
        Text("UniversityRequestFileName", displayName: "University Request File Name"),
    ];
    private static IReadOnlyList<Dictionary<string, object?>> TrainingDocumentColumns(string training) =>
        [Lookup("TrainingRequest", training), Choice("DocumentType", ["Transcript", "University Request"], true), Text("ApplicantEmail", true), Date("UploadedAt", true), Boolean("IsCurrent")];
}
