using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

public class DentalRecordTooth : Entity<Guid>
{
    public Guid DentalRecordId { get; private set; }
    public int ToothNumber { get; private set; } // Tooth number according to FDI notation (11-48 for adult, 51-85 for child)
    public DateTime CreatedAt { get; private set; }

    // Navigation property
    public DentalRecord DentalRecord { get; private set; } = null!;

    private DentalRecordTooth() { } // For EF Core

    public DentalRecordTooth(
        Guid id,
        Guid dentalRecordId,
        int toothNumber)
    {
        // Validate tooth number based on FDI notation
        // Adult teeth: 11-18, 21-28, 31-38, 41-48
        // Child teeth: 51-55, 61-65, 71-75, 81-85
        if (!IsValidToothNumber(toothNumber))
            throw new ArgumentException($"Invalid tooth number: {toothNumber}. Must be between 11-18, 21-28, 31-38, 41-48 (adult) or 51-55, 61-65, 71-75, 81-85 (child)", nameof(toothNumber));

        Id = id;
        DentalRecordId = dentalRecordId;
        ToothNumber = toothNumber;
        CreatedAt = DateTime.UtcNow;
    }

    private static bool IsValidToothNumber(int toothNumber)
    {
        // Adult teeth
        if ((toothNumber >= 11 && toothNumber <= 18) ||
            (toothNumber >= 21 && toothNumber <= 28) ||
            (toothNumber >= 31 && toothNumber <= 38) ||
            (toothNumber >= 41 && toothNumber <= 48))
            return true;

        // Child teeth
        if ((toothNumber >= 51 && toothNumber <= 55) ||
            (toothNumber >= 61 && toothNumber <= 65) ||
            (toothNumber >= 71 && toothNumber <= 75) ||
            (toothNumber >= 81 && toothNumber <= 85))
            return true;

        return false;
    }

    public static bool IsAdultTooth(int toothNumber)
    {
        return (toothNumber >= 11 && toothNumber <= 18) ||
               (toothNumber >= 21 && toothNumber <= 28) ||
               (toothNumber >= 31 && toothNumber <= 38) ||
               (toothNumber >= 41 && toothNumber <= 48);
    }
}









