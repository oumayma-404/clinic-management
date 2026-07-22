using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.ValueObjects;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class ProcedureType : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public string Name { get; private set; }
    public int DefaultDurationMinutes { get; private set; }
    public decimal? DefaultCost { get; private set; }
    public ColorHex Color { get; private set; }
    public string? Description { get; private set; }
    /// <summary>Odontogram state a dental act of this procedure produces (null = no tooth-state change). Editable.</summary>
    public ToothCondition? ResultingCondition { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Navigation property
    public ICollection<Appointment> Appointments { get; private set; } = new List<Appointment>();

    private ProcedureType() { } // For EF Core

    public ProcedureType(
        Guid id,
        Guid clinicId,
        string name,
        int defaultDurationMinutes,
        ColorHex color,
        string? description = null,
        decimal? defaultCost = null,
        ToothCondition? resultingCondition = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or empty", nameof(name));

        if (defaultDurationMinutes <= 0)
            throw new ArgumentException("Default duration must be greater than 0", nameof(defaultDurationMinutes));

        if (defaultDurationMinutes >= 480)
            throw new ArgumentException("Default duration must be less than 480 minutes (8 hours)", nameof(defaultDurationMinutes));

        if (color == null)
            throw new ArgumentNullException(nameof(color));

        if (defaultCost.HasValue && defaultCost.Value < 0)
            throw new ArgumentException("Default cost cannot be negative", nameof(defaultCost));

        Id = id;
        ClinicId = clinicId;
        Name = name.Trim();
        DefaultDurationMinutes = defaultDurationMinutes;
        DefaultCost = defaultCost;
        Color = color;
        Description = description?.Trim();
        ResultingCondition = resultingCondition == ToothCondition.Sain ? null : resultingCondition;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateResultingCondition(ToothCondition? resultingCondition)
    {
        ResultingCondition = resultingCondition == ToothCondition.Sain ? null : resultingCondition;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or empty", nameof(name));

        Name = name.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDefaultDuration(int defaultDurationMinutes)
    {
        if (defaultDurationMinutes <= 0)
            throw new ArgumentException("Default duration must be greater than 0", nameof(defaultDurationMinutes));

        if (defaultDurationMinutes >= 480)
            throw new ArgumentException("Default duration must be less than 480 minutes (8 hours)", nameof(defaultDurationMinutes));

        DefaultDurationMinutes = defaultDurationMinutes;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateColor(ColorHex color)
    {
        if (color == null)
            throw new ArgumentNullException(nameof(color));

        Color = color;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDescription(string? description)
    {
        Description = description?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDefaultCost(decimal? defaultCost)
    {
        if (defaultCost.HasValue && defaultCost.Value < 0)
            throw new ArgumentException("Default cost cannot be negative", nameof(defaultCost));

        DefaultCost = defaultCost;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Checks if this procedure type is used by any future appointments
    /// </summary>
    public bool IsUsedByFutureAppointments(IEnumerable<Appointment> appointments)
    {
        var now = DateTime.UtcNow;
        return appointments.Any(apt => 
            apt.ProcedureTypeId == Id && 
            apt.AppointmentDateTime > now &&
            apt.Status != AppointmentStatus.Cancelled &&
            apt.Status != AppointmentStatus.Completed);
    }
}

