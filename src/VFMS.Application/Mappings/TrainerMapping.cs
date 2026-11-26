using Mapster;
using VFMS.Application.Services;
using VFMS.Contracts.Trainers;
using VFMS.Domain.Entities;

namespace VFMS.Application.Mappings;

public sealed class TrainerMapping : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // Detail-only fields and resend eligibility are filled by TrainerService.Application.
        config.NewConfig<TrainerApplication, TrainerApplicationResponse>()
            .Map(d => d.Status, s => s.ApplicationStatus.ToString())
            .Map(d => d.Certificates, s => TrainerService.Docs(s.CertificatesJson, "/api/trainers/applications/" + s.Id, false))
            // Ignore() breaks positional-record constructor mapping, so use explicit defaults.
            .Map(d => d.SubmissionHistory, _ => (IReadOnlyList<TrainerSubmissionAttempt>?)null)
            .Map(d => d.InterviewNote, _ => (string?)null)
            .Map(d => d.InterviewedAt, _ => (DateTime?)null)
            .Map(d => d.CanResendSetup, _ => false)
            .Map(d => d.AccountMatch, _ => (TrainerAccountMatch?)null)
            // TrainerService fills the role name from TrainerRoleId; the navigation is not loaded.
            .Map(d => d.TrainerRole, _ => (string?)null);
    }
}
