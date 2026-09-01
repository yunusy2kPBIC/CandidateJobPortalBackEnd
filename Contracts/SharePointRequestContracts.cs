using System.ComponentModel.DataAnnotations;
using CandidatePortal.Api.Models;

namespace CandidatePortal.Api.Contracts;

public sealed class RecruitmentRequestCreate : IValidatableObject
{
    [Required, MinLength(1), MaxLength(180)] public string PreferredPosition { get; init; } = "";
    [Required, MinLength(1), MaxLength(255)] public string Name { get; init; } = "";
    [Required, MinLength(1), MaxLength(100)] public string Nationality { get; init; } = "";
    [Required] public string Gender { get; init; } = "";
    [Required] public string DriverLicenseType { get; init; } = "";
    [Required, MinLength(7), MaxLength(40)] public string MobileNumber { get; init; } = "";
    [Required, EmailAddress, MaxLength(255)] public string EmailAddress { get; init; } = "";
    [Required, MinLength(7), MaxLength(20)] public string IqamaNumber { get; init; } = "";
    [Required, MinLength(1), MaxLength(150)] public string IqamaProfession { get; init; } = "";
    [MaxLength(180)] public string CurrentEmployer { get; init; } = "Not currently employed";
    public DateOnly DateOfBirth { get; init; }
    [Required, MinLength(1), MaxLength(100)] public string City { get; init; } = "";
    public bool AcceptWorkInAnotherCity { get; init; }
    [Required] public string Qualification { get; init; } = "";
    [Range(0, 100_000_000)] public double CurrentSalary { get; init; }
    [MaxLength(5000)] public string Comments { get; init; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        SharePointRequestValidation.ValidateRecruitment(
            Gender, DriverLicenseType, MobileNumber, IqamaNumber, Qualification, DateOfBirth,
            requireBirthDate: true);

    public Dictionary<string, object?> ToFields() => new()
    {
        ["Title"] = Name.Trim(),
        ["PreferredPosition"] = PreferredPosition.Trim(),
        ["Nationality"] = Nationality.Trim(),
        ["Gender"] = Gender,
        ["DriverLicenseType"] = DriverLicenseType,
        ["MobileNumber"] = MobileNumber.Trim(),
        ["EmailAddress"] = EmailAddress.Trim().ToLowerInvariant(),
        ["IqamaNumber"] = IqamaNumber.Trim(),
        ["IqamaProfession"] = IqamaProfession.Trim(),
        ["CurrentEmployer"] = CurrentEmployer.Trim(),
        ["DateOfBirth"] = DateOfBirth.ToString("yyyy-MM-dd"),
        ["City"] = City.Trim(),
        ["AcceptWorkInAnotherCity"] = AcceptWorkInAnotherCity,
        ["Qualification"] = Qualification,
        ["CurrentSalary"] = CurrentSalary,
        ["Comments"] = Comments.Trim(),
    };
}

public sealed class RecruitmentRequestUpdate : IValidatableObject
{
    [MinLength(1), MaxLength(180)] public string? PreferredPosition { get; init; }
    [MinLength(1), MaxLength(255)] public string? Name { get; init; }
    [MinLength(1), MaxLength(100)] public string? Nationality { get; init; }
    public string? Gender { get; init; }
    public string? DriverLicenseType { get; init; }
    [MinLength(7), MaxLength(40)] public string? MobileNumber { get; init; }
    [EmailAddress, MaxLength(255)] public string? EmailAddress { get; init; }
    [MinLength(7), MaxLength(20)] public string? IqamaNumber { get; init; }
    [MinLength(1), MaxLength(150)] public string? IqamaProfession { get; init; }
    [MaxLength(180)] public string? CurrentEmployer { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    [MinLength(1), MaxLength(100)] public string? City { get; init; }
    public bool? AcceptWorkInAnotherCity { get; init; }
    public string? Qualification { get; init; }
    [Range(0, 100_000_000)] public double? CurrentSalary { get; init; }
    [MaxLength(5000)] public string? Comments { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        SharePointRequestValidation.ValidateRecruitment(
            Gender, DriverLicenseType, MobileNumber, IqamaNumber, Qualification, DateOfBirth);

    public Dictionary<string, object?> ToFields() => SharePointFieldMappings.OptionalFields(
        ("PreferredPosition", PreferredPosition), ("Title", Name), ("Nationality", Nationality),
        ("Gender", Gender), ("DriverLicenseType", DriverLicenseType), ("MobileNumber", MobileNumber),
        ("EmailAddress", EmailAddress?.ToLowerInvariant()), ("IqamaNumber", IqamaNumber),
        ("IqamaProfession", IqamaProfession), ("CurrentEmployer", CurrentEmployer),
        ("DateOfBirth", DateOfBirth?.ToString("yyyy-MM-dd")), ("City", City),
        ("AcceptWorkInAnotherCity", AcceptWorkInAnotherCity), ("Qualification", Qualification),
        ("CurrentSalary", CurrentSalary), ("Comments", Comments));
}

public sealed record RecruitmentRequestResponse(
    string Id,
    string? WebUrl,
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    string PreferredPosition,
    string Name,
    string Nationality,
    string Gender,
    string DriverLicenseType,
    string MobileNumber,
    string EmailAddress,
    string IqamaNumber,
    string IqamaProfession,
    string CurrentEmployer,
    DateOnly DateOfBirth,
    string City,
    bool AcceptWorkInAnotherCity,
    string Qualification,
    double CurrentSalary,
    string Comments)
{
    public static RecruitmentRequestResponse FromItem(SharePointItemResponse item) => new(
        item.Id, item.WebUrl, item.CreatedAt, item.UpdatedAt,
        SharePointResponseFields.Text(item, "", "Preferred Position", "PreferredPosition"),
        SharePointResponseFields.Text(item, "", "Name", "Title"),
        SharePointResponseFields.Text(item, "", "Nationality"),
        SharePointResponseFields.Text(item, "Other", "Gender"),
        SharePointResponseFields.Text(item, "None", "Driver License Type", "DriverLicenseType"),
        SharePointResponseFields.Text(item, "", "Mobile Number", "MobileNumber"),
        SharePointResponseFields.Text(item, "", "Email Address", "EmailAddress"),
        SharePointResponseFields.Text(item, "", "ID/Iqama Number", "IqamaNumber"),
        SharePointResponseFields.Text(item, "", "Iqama Profession", "IqamaProfession"),
        SharePointResponseFields.Text(item, "", "Current Employer", "CurrentEmployer"),
        SharePointResponseFields.SaudiDate(item, "Date of Birth", "DateOfBirth"),
        SharePointResponseFields.Text(item, "", "City"),
        SharePointResponseFields.Boolean(item, false, "Accept Work in Another City", "AcceptWorkInAnotherCity"),
        SharePointResponseFields.Text(item, "Other", "Qualification"),
        SharePointResponseFields.Number(item, 0, "Current Salary (SAR)", "CurrentSalary"),
        SharePointResponseFields.Text(item, "", "Comments"));
}

public sealed class CooperativeTrainingCreateRequest : IValidatableObject
{
    [Required, MinLength(1), MaxLength(100)] public string FirstName { get; init; } = "";
    [Required, MinLength(1), MaxLength(100)] public string LastName { get; init; } = "";
    [Required, MinLength(7), MaxLength(20)] public string IdNumber { get; init; } = "";
    [Required, MinLength(7), MaxLength(40)] public string MobileNumber { get; init; } = "";
    [Required, EmailAddress, MaxLength(255)] public string Email { get; init; } = "";
    [Required] public string Gender { get; init; } = "";
    [Range(1, 24)] public int TrainingDuration { get; init; }
    [Required] public string Semester { get; init; } = "";
    public DateOnly TrainingStartingDate { get; init; }
    [Required, MinLength(1), MaxLength(180)] public string TrainingSupervisorName { get; init; } = "";
    [Required, MinLength(7), MaxLength(40)] public string TrainingSupervisorNumber { get; init; } = "";
    [Required, EmailAddress, MaxLength(255)] public string TrainingSupervisorEmail { get; init; } = "";
    [Required, MinLength(1), MaxLength(200)] public string UniversityCollege { get; init; } = "";
    [Required] public string Qualification { get; init; } = "";
    [Required, MinLength(1), MaxLength(180)] public string Major { get; init; } = "";
    public int GpaScale { get; init; }
    [Range(0, 5)] public double CumulativeGpa { get; init; }
    [Required] public string EnglishLevel { get; init; } = "";
    [Required, MinLength(1), MaxLength(100)] public string DesiredCityForTraining { get; init; } = "";
    [Required, MinLength(1), MaxLength(100)] public string CurrentCityOfResidency { get; init; } = "";
    public bool Disability { get; init; }
    public bool DeclarationAccepted { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        SharePointRequestValidation.ValidateTraining(this);

    public Dictionary<string, object?> ToFields() => new()
    {
        ["Title"] = $"{FirstName} {LastName}".Trim(),
        ["FirstName"] = FirstName.Trim(),
        ["LastName"] = LastName.Trim(),
        ["IdNumber"] = IdNumber.Trim(),
        ["MobileNumber"] = MobileNumber.Trim(),
        ["Email"] = Email.Trim().ToLowerInvariant(),
        ["Gender"] = Gender,
        ["TrainingDuration"] = TrainingDuration,
        ["Semester"] = Semester,
        ["TrainingStartingDate"] = TrainingStartingDate.ToString("yyyy-MM-dd"),
        ["TrainingSupervisorName"] = TrainingSupervisorName.Trim(),
        ["TrainingSupervisorNumber"] = TrainingSupervisorNumber.Trim(),
        ["TrainingSupervisorEmail"] = TrainingSupervisorEmail.Trim().ToLowerInvariant(),
        ["UniversityCollege"] = UniversityCollege.Trim(),
        ["Qualification"] = Qualification,
        ["Major"] = Major.Trim(),
        ["GpaScale"] = GpaScale.ToString(),
        ["CumulativeGpa"] = CumulativeGpa,
        ["EnglishLevel"] = EnglishLevel,
        ["DesiredCityForTraining"] = DesiredCityForTraining.Trim(),
        ["CurrentCityOfResidency"] = CurrentCityOfResidency.Trim(),
        ["Disability"] = Disability,
        ["DeclarationAccepted"] = DeclarationAccepted,
    };
}

public sealed class CooperativeTrainingUpdateRequest
{
    [MinLength(1), MaxLength(100)] public string? FirstName { get; init; }
    [MinLength(1), MaxLength(100)] public string? LastName { get; init; }
    [MinLength(7), MaxLength(20)] public string? IdNumber { get; init; }
    [MinLength(7), MaxLength(40)] public string? MobileNumber { get; init; }
    [EmailAddress, MaxLength(255)] public string? Email { get; init; }
    public string? Gender { get; init; }
    [Range(1, 24)] public int? TrainingDuration { get; init; }
    public string? Semester { get; init; }
    public DateOnly? TrainingStartingDate { get; init; }
    [MinLength(1), MaxLength(180)] public string? TrainingSupervisorName { get; init; }
    [MinLength(7), MaxLength(40)] public string? TrainingSupervisorNumber { get; init; }
    [EmailAddress, MaxLength(255)] public string? TrainingSupervisorEmail { get; init; }
    [MinLength(1), MaxLength(200)] public string? UniversityCollege { get; init; }
    public string? Qualification { get; init; }
    [MinLength(1), MaxLength(180)] public string? Major { get; init; }
    public int? GpaScale { get; init; }
    [Range(0, 5)] public double? CumulativeGpa { get; init; }
    public string? EnglishLevel { get; init; }
    [MinLength(1), MaxLength(100)] public string? DesiredCityForTraining { get; init; }
    [MinLength(1), MaxLength(100)] public string? CurrentCityOfResidency { get; init; }
    public bool? Disability { get; init; }
    public bool? DeclarationAccepted { get; init; }

    public CooperativeTrainingCreateRequest ApplyTo(CooperativeTrainingResponse value) => new()
    {
        FirstName = FirstName ?? value.FirstName,
        LastName = LastName ?? value.LastName,
        IdNumber = IdNumber ?? value.IdNumber,
        MobileNumber = MobileNumber ?? value.MobileNumber,
        Email = Email ?? value.Email,
        Gender = Gender ?? value.Gender,
        TrainingDuration = TrainingDuration ?? value.TrainingDuration,
        Semester = Semester ?? value.Semester,
        TrainingStartingDate = TrainingStartingDate ?? value.TrainingStartingDate,
        TrainingSupervisorName = TrainingSupervisorName ?? value.TrainingSupervisorName,
        TrainingSupervisorNumber = TrainingSupervisorNumber ?? value.TrainingSupervisorNumber,
        TrainingSupervisorEmail = TrainingSupervisorEmail ?? value.TrainingSupervisorEmail,
        UniversityCollege = UniversityCollege ?? value.UniversityCollege,
        Qualification = Qualification ?? value.Qualification,
        Major = Major ?? value.Major,
        GpaScale = GpaScale ?? value.GpaScale,
        CumulativeGpa = CumulativeGpa ?? value.CumulativeGpa,
        EnglishLevel = EnglishLevel ?? value.EnglishLevel,
        DesiredCityForTraining = DesiredCityForTraining ?? value.DesiredCityForTraining,
        CurrentCityOfResidency = CurrentCityOfResidency ?? value.CurrentCityOfResidency,
        Disability = Disability ?? value.Disability,
        DeclarationAccepted = DeclarationAccepted ?? value.DeclarationAccepted,
    };
}

public sealed record CooperativeTrainingResponse(
    string Id,
    string? WebUrl,
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    string FirstName,
    string LastName,
    string IdNumber,
    string MobileNumber,
    string Email,
    string Gender,
    int TrainingDuration,
    string Semester,
    DateOnly TrainingStartingDate,
    string TrainingSupervisorName,
    string TrainingSupervisorNumber,
    string TrainingSupervisorEmail,
    string UniversityCollege,
    string Qualification,
    string Major,
    int GpaScale,
    double CumulativeGpa,
    string EnglishLevel,
    string DesiredCityForTraining,
    string CurrentCityOfResidency,
    bool Disability,
    bool DeclarationAccepted,
    string? TranscriptUrl,
    string? TranscriptName,
    string? UniversityRequestUrl,
    string? UniversityRequestName)
{
    public static CooperativeTrainingResponse FromItem(SharePointItemResponse item) => new(
        item.Id, item.WebUrl, item.CreatedAt, item.UpdatedAt,
        SharePointResponseFields.Text(item, "", "First Name", "FirstName"),
        SharePointResponseFields.Text(item, "", "Last Name", "LastName"),
        SharePointResponseFields.Text(item, "", "ID Number", "IdNumber"),
        SharePointResponseFields.Text(item, "", "Mobile Number", "MobileNumber"),
        SharePointResponseFields.Text(item, "", "Email"),
        SharePointResponseFields.Text(item, "Other", "Gender"),
        SharePointResponseFields.Integer(item, 1, "Training Duration (Months)", "TrainingDuration"),
        SharePointResponseFields.Text(item, "First Semester", "Semester"),
        SharePointResponseFields.SaudiDate(item, "Training Starting Date", "TrainingStartingDate"),
        SharePointResponseFields.Text(item, "", "Training Supervisor Name", "TrainingSupervisorName"),
        SharePointResponseFields.Text(item, "", "Training Supervisor Number", "TrainingSupervisorNumber"),
        SharePointResponseFields.Text(item, "", "Training Supervisor Email", "TrainingSupervisorEmail"),
        SharePointResponseFields.Text(item, "", "University / College", "UniversityCollege"),
        SharePointResponseFields.Text(item, "Other", "Qualification"),
        SharePointResponseFields.Text(item, "", "Major"),
        SharePointResponseFields.Integer(item, 4, "GPA Scale", "GpaScale"),
        SharePointResponseFields.Number(item, 0, "Cumulative GPA", "CumulativeGpa"),
        SharePointResponseFields.Text(item, "Intermediate", "English Level", "EnglishLevel"),
        SharePointResponseFields.Text(item, "", "Desired City for Training", "DesiredCityForTraining"),
        SharePointResponseFields.Text(item, "", "Current City of Residency", "CurrentCityOfResidency"),
        SharePointResponseFields.Boolean(item, false, "Disability"),
        SharePointResponseFields.Boolean(item, false, "Declaration Accepted", "DeclarationAccepted"),
        SharePointResponseFields.NullableText(item, "Transcript URL", "TranscriptUrl"),
        SharePointResponseFields.NullableText(item, "Transcript File Name", "TranscriptFileName"),
        SharePointResponseFields.NullableText(item, "University Request URL", "UniversityRequestUrl"),
        SharePointResponseFields.NullableText(item, "University Request File Name", "UniversityRequestFileName"));
}

internal static class SharePointRequestValidation
{
    private static readonly HashSet<string> Genders = ["Male", "Female", "Other"];
    private static readonly HashSet<string> Licenses = ["Saudi License", "Valid GCC License", "Other License", "None"];
    private static readonly HashSet<string> Qualifications =
        ["High School", "Diploma", "Bachelor's Degree", "Master's Degree", "Doctorate", "Other"];
    private static readonly HashSet<string> Semesters = ["First Semester", "Second Semester", "Summer Semester"];
    private static readonly HashSet<string> EnglishLevels = ["Beginner", "Intermediate", "Advanced", "Fluent"];

    public static IEnumerable<ValidationResult> ValidateRecruitment(
        string? gender, string? license, string? mobile, string? iqama, string? qualification,
        DateOnly? birthDate, bool requireBirthDate = false)
    {
        if (gender is not null && !Genders.Contains(gender)) yield return Error("gender is invalid", "Gender");
        if (license is not null && !Licenses.Contains(license)) yield return Error("driver_license_type is invalid", "DriverLicenseType");
        if (qualification is not null && !Qualifications.Contains(qualification)) yield return Error("qualification is invalid", "Qualification");
        if (mobile is not null && !ValidPhone(mobile)) yield return Error("Enter a valid mobile number", "MobileNumber");
        if (iqama is not null && !ValidId(iqama)) yield return Error("ID/Iqama number must contain 7-20 digits and cannot start with zero", "IqamaNumber");
        if ((requireBirthDate && (birthDate is null || birthDate == default)) ||
            (birthDate is not null && birthDate >= DateOnly.FromDateTime(DateTime.Today)))
            yield return Error("Date of birth must be in the past", "DateOfBirth");
    }

    public static IEnumerable<ValidationResult> ValidateTraining(CooperativeTrainingCreateRequest value)
    {
        if (!Genders.Contains(value.Gender)) yield return Error("gender is invalid", "Gender");
        if (!Semesters.Contains(value.Semester)) yield return Error("semester is invalid", "Semester");
        if (!Qualifications.Contains(value.Qualification)) yield return Error("qualification is invalid", "Qualification");
        if (!EnglishLevels.Contains(value.EnglishLevel)) yield return Error("english_level is invalid", "EnglishLevel");
        if (!ValidId(value.IdNumber)) yield return Error("ID number must contain 7-20 digits and cannot start with zero", "IdNumber");
        if (!ValidPhone(value.MobileNumber)) yield return Error("Enter a valid mobile number", "MobileNumber");
        if (!ValidPhone(value.TrainingSupervisorNumber)) yield return Error("Enter a valid supervisor number", "TrainingSupervisorNumber");
        if (value.TrainingStartingDate == default) yield return Error("Training starting date is required", "TrainingStartingDate");
        if (value.GpaScale is not (4 or 5)) yield return Error("GPA scale must be 4 or 5", "GpaScale");
        if (value.CumulativeGpa > value.GpaScale) yield return Error("Cumulative GPA cannot exceed the selected GPA scale", "CumulativeGpa");
        if (!value.DeclarationAccepted) yield return Error("The accuracy declaration must be accepted", "DeclarationAccepted");
    }

    private static bool ValidPhone(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return value.All(character => char.IsDigit(character) || " +()-".Contains(character)) && digits.Length is >= 7 and <= 20;
    }

    private static bool ValidId(string value) => value.Length is >= 7 and <= 20 && value[0] != '0' && value.All(char.IsDigit);
    private static ValidationResult Error(string message, string member) => new(message, [member]);
}

internal static class SharePointResponseFields
{
    public static string Text(SharePointItemResponse item, string fallback, params string[] names) =>
        NullableText(item, names) ?? fallback;

    public static string? NullableText(SharePointItemResponse item, params string[] names) =>
        Value(item, names) is { } value ? Convert.ToString(value) : null;

    public static bool Boolean(SharePointItemResponse item, bool fallback, params string[] names) =>
        Value(item, names) is { } value && bool.TryParse(Convert.ToString(value), out var parsed) ? parsed : fallback;

    public static int Integer(SharePointItemResponse item, int fallback, params string[] names) =>
        Value(item, names) is { } value && int.TryParse(Convert.ToString(value), out var parsed) ? parsed : fallback;

    public static double Number(SharePointItemResponse item, double fallback, params string[] names) =>
        Value(item, names) is { } value && double.TryParse(
            Convert.ToString(value), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    public static DateOnly SaudiDate(SharePointItemResponse item, params string[] names)
    {
        var text = Convert.ToString(Value(item, names)) ?? throw new InvalidOperationException("SharePoint date field is missing");
        if (!text.Contains('T') && DateOnly.TryParse(text, out var date)) return date;
        if (DateTimeOffset.TryParse(text, out var timestamp))
            return DateOnly.FromDateTime(timestamp.ToOffset(TimeSpan.FromHours(3)).DateTime);
        throw new InvalidOperationException($"SharePoint date value '{text}' is invalid");
    }

    private static object? Value(SharePointItemResponse item, IEnumerable<string> names)
    {
        foreach (var name in names)
            if (item.Fields.TryGetValue(name, out var value)) return value;
        return null;
    }
}
