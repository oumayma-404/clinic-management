using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.DentalActs;

/// <summary>Maps <see cref="DentalActCode"/> entities to their DTOs.</summary>
public static class DentalActMappingExtensions
{
    public static DentalActDto ToDto(this DentalActCode act) => new()
    {
        Id = act.Id,
        CodeActe = act.CodeActe,
        DesignationFr = act.DesignationFr,
        LettreCle = act.LettreCle,
        Coefficient = act.Coefficient,
        Category = act.Category,
        DefaultFee = act.DefaultFee,
        RequiresAccordPrealable = act.RequiresAccordPrealable,
        IsActive = act.IsActive,
        IsProvisional = act.IsProvisional,
        Version = act.Version
    };
}
