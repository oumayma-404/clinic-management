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
        // `FdiTooth` is the authority; this class used to hold a second copy of the same range table.
        if (!FdiTooth.IsValid(toothNumber))
            throw new ArgumentException(FdiTooth.NotAToothNumber, nameof(toothNumber));

        Id = id;
        DentalRecordId = dentalRecordId;
        ToothNumber = toothNumber;
        CreatedAt = DateTime.UtcNow;
    }


    public static bool IsAdultTooth(int toothNumber)
    {
        return (toothNumber >= 11 && toothNumber <= 18) ||
               (toothNumber >= 21 && toothNumber <= 28) ||
               (toothNumber >= 31 && toothNumber <= 38) ||
               (toothNumber >= 41 && toothNumber <= 48);
    }
}









