using CandidatePortal.Api.Configuration;
using CandidatePortal.Api.Contracts;

namespace CandidatePortal.Api.Services;

public sealed class PrivacyNoticeService(PortalOptions options)
{
    public string CurrentVersion => options.PrivacyPolicyVersion;

    public PrivacyNoticeResponse Current() => new(
        "PBICareerPosting Candidate Portal Privacy Notice",
        options.PrivacyPolicyVersion,
        options.PrivacyEffectiveDate,
        options.PrivacyContactEmail,
        [
            new PrivacySectionResponse(
                "Information we collect",
                "We collect the account, contact, profile, CV, application, and Cooperative Training information you provide. We also record limited technical information, such as your IP address and browser details, when you accept this notice."),
            new PrivacySectionResponse(
                "How we use your information",
                "We use your information to create and secure your account, assess job or training applications, communicate application updates, support recruitment administration, and meet legal or operational requirements."),
            new PrivacySectionResponse(
                "Who can access your information",
                "Access is limited to authorized recruitment and administration users. Information may be stored in approved Microsoft 365, SharePoint, database, and infrastructure services used to operate this portal."),
            new PrivacySectionResponse(
                "Retention and security",
                "Information is retained only for as long as needed for recruitment, training, security, and applicable record-keeping requirements. Administrative, technical, and access controls are used to protect portal information."),
            new PrivacySectionResponse(
                "Your choices and rights",
                "You may request access to or correction of your information and may ask questions about retention or deletion, subject to applicable requirements. Contact the privacy team using the address shown below."),
            new PrivacySectionResponse(
                "Changes to this notice",
                "When this notice changes materially, a new version will be published. The portal records the version you accepted so that your consent evidence remains auditable."),
        ]);
}
