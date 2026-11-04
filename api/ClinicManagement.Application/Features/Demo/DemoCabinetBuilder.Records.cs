using System.Text.Json;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Documents;
using ClinicManagement.Application.Features.Documents.Commands;
using ClinicManagement.Application.Features.Files.Commands;
using ClinicManagement.Application.Features.LabOrders.Commands;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Stock.Commands;
using ClinicManagement.Application.Features.Suppliers.Commands;

namespace ClinicManagement.Application.Features.Demo;

/// <summary>
/// Part C of the scenario (spec FR-6 « Documents », « Files », « Stock and lab », « Worklists »): what a cabinet keeps
/// beside its agenda and its money.
/// </summary>
public sealed partial class DemoCabinetBuilder
{
    private Guid _labSupplierId;

    private async Task BuildRecordsAsync(CancellationToken ct)
    {
        await DocumentsAsync(ct);
        await FilesAsync(ct);
        await StockAndLabAsync(ct);
        await SuiteToPlanAsync(ct);
    }

    // ---- documents -----------------------------------------------------------------------------------------------

    private async Task DocumentsAsync(CancellationToken ct)
    {
        // Ross Geller: pénicilline allergy, so clindamycine — and a panoramique, on its own demande d'examens.
        var ross = PatientNamed("Ross Geller");
        var act = Named("Consultation / examen bucco-dentaire");
        Must(await _seed.SendAsAsync(_monica, new CreateDentalRecordCommand
        {
            PatientId = ross.Id,
            InterventionDate = ClinicClock.ToUtc(WorkingDayBefore(-2) + new TimeSpan(16, 30, 0)),
            DoctorId = _monicaDoctorId,
            IsAdultTeeth = true,
            Acts = new() { new() { ProcedureTypeId = act.Id, ProcedureName = act.Name, Cost = act.DefaultCost ?? 0m, UnitCost = act.DefaultCost ?? 0m } },
            AmountPaid = act.DefaultCost ?? 0m,
            PaymentMethod = "Cash",
            Notes = new() { "Douleur 37 — prescription adaptée à l'allergie" },
            Prescription = new PrescriptionInput
            {
                Lines = new()
                {
                    new() { Kind = "medicament", Name = "Clindamycine", Dosage = "300 mg", Dose = "1 gélule", TimesPerDay = "3", Duration = "7", DurationUnit = "jours" },
                    new() { Kind = "medicament", Name = "Paracétamol", Dosage = "1 g", Dose = "1 comprimé", TimesPerDay = "3", Duration = "5", DurationUnit = "jours" },
                    new() { Kind = "examen", Name = "Radiographie panoramique dentaire" },
                },
            },
        }, ct), "enregistrer l'ordonnance de Ross Geller");

        var joey = PatientNamed("Joey Tribbiani");
        await DocumentAsync(_monica, _monicaDoctorId, joey, "certificat", -1, new Dictionary<string, object?>
        {
            ["date"] = DayKey(WorkingDayBefore(-1)),
            ["startDate"] = DayKey(WorkingDayBefore(-1)),
            ["duration"] = "2",
            ["durationUnit"] = "jours",
            ["patientCivility"] = "M.",
        }, ct);

        var frank = PatientNamed("Frank Buffay");
        await DocumentAsync(_chandler, _chandlerDoctorId, frank, "arret-travail", -3, new Dictionary<string, object?>
        {
            ["date"] = DayKey(WorkingDayBefore(-3)),
            ["patientFirstName"] = frank.Spec.FirstName,
            ["patientLastName"] = frank.Spec.LastName,
            ["days"] = "3",
            ["fromDate"] = DayKey(WorkingDayBefore(-3)),
            ["doctorName"] = DemoScenario.SecondDentistFullName,
            ["doctorQuality"] = DoctorSpecialtyLabels.Label(DemoScenario.DentistSpecialty),
            ["doctorOrdreNumber"] = "0000",
            ["city"] = DemoScenario.City,
            ["signPlace"] = DemoScenario.City,
        }, ct);
    }

    private async Task DocumentAsync(
        DemoMember dentist, Guid doctorId, (DemoPatientSpec Spec, Guid Id) patient, string type, int daysFromToday,
        Dictionary<string, object?> content, CancellationToken ct)
    {
        var doctorName = dentist == _monica
            ? DemoScenario.DentistFullName
            : DemoScenario.SecondDentistFullName;
        Must(await _seed.SendAsAsync(dentist, new CreateMedicalDocumentCommand
        {
            PatientId = patient.Id,
            DocumentType = type,
            DocumentDate = ClinicClock.ToUtc(WorkingDayBefore(daysFromToday) + new TimeSpan(11, 0, 0)),
            ContentJson = JsonSerializer.Serialize(content),
            ClinicName = DemoScenario.CabinetName,
            ClinicAddress = $"{DemoScenario.Address}, {DemoScenario.City}",
            ClinicPhone = DemoScenario.Phone,
            DoctorName = doctorName,
            DoctorSpecialty = DoctorSpecialtyLabels.Label(DemoScenario.DentistSpecialty),
            IssuingDoctorId = doctorId,
        }, ct), $"émettre le document « {type} » de {patient.Spec.FirstName}");
    }

    // ---- files ---------------------------------------------------------------------------------------------------

    private async Task FilesAsync(CancellationToken ct)
    {
        foreach (var (patient, file, name, description) in new[]
                 {
                     ("Ross Geller", "pano.jpg", "Panoramique.jpg", "Panoramique de contrôle"),
                     ("Leonard Green", "pano.jpg", "Panoramique avant bridge.jpg", "Bilan avant le bridge 14-16"),
                     ("Jill Green", "photo-sourire.jpg", "Sourire avant facettes.jpg", "Photo de face — projet esthétique"),
                     ("Jill Green", "photo-sourire-detail.jpg", "Sourire — vue rapprochée.jpg", "Incisives 11 et 21 avant facettes"),
                 })
        {
            await using var bytes = Asset(file);
            Must(await _seed.SendAsAsync(_rachel, new UploadPatientFileCommand
            {
                PatientId = PatientNamed(patient).Id,
                FileName = name,
                FileSize = bytes.Length,
                FileStream = bytes,
                Description = description,
            }, ct), $"déposer « {name} »");
        }
    }

    // ---- stock, fournisseurs, laboratoire ------------------------------------------------------------------------

    private async Task StockAndLabAsync(CancellationToken ct)
    {
        var depot = Must(await _seed.SendAsAsync(_rachel, new CreateSupplierCommand
        {
            Name = "Dentalex Tunisie",
            Category = "Consommables médicaux",
            Address = "Zone industrielle, Ben Arous",
        }, ct), "créer le fournisseur Dentalex");
        var lab = Must(await _seed.SendAsAsync(_rachel, new CreateSupplierCommand
        {
            Name = "Laboratoire Dentalab",
            Category = "Fournitures de laboratoire",
            Address = "Rue de Marseille, Tunis",
        }, ct), "créer le laboratoire Dentalab");
        _labSupplierId = lab.Id;

        foreach (var (name, category, unit, current, minimum, price) in new[]
                 {
                     ("Gants nitrile taille M", "Protection (EPI)", "boîte", 2, 6, 18m),
                     ("Composite A2", "Consommables médicaux", "seringue", 9, 4, 42m),
                     ("Masques chirurgicaux", "Protection (EPI)", "boîte", 12, 5, 9m),
                 })
        {
            Must(await _seed.SendAsAsync(_rachel, new CreateStockItemCommand
            {
                Name = name, Category = category, Unit = unit, CurrentStock = current, MinimumStockLevel = minimum,
                UnitPrice = price, SupplierId = depot.Id,
            }, ct), $"créer l'article « {name} »");
        }

        var anaesthetic = Must(await _seed.SendAsAsync(_rachel, new CreateStockItemCommand
        {
            Name = "Articaïne 4 % adrénalinée", Category = "Médicaments", Unit = "carpule", CurrentStock = 0,
            MinimumStockLevel = 20, UnitPrice = 1.2m, SupplierId = depot.Id,
        }, ct), "créer l'anesthésique");
        // One lot runs out in three weeks — « lots bientôt périmés ».
        Must(await _seed.SendAsAsync(_rachel, new RestockStockItemCommand
        {
            Id = anaesthetic.Id,
            Quantity = 50,
            ExpiryDate = DateTime.SpecifyKind(_today.AddDays(21), DateTimeKind.Utc),
            BatchNumber = "ART-2311",
            Reason = "Livraison Dentalex",
        }, ct), "réceptionner le lot d'articaïne");

        // Ross's crown is late at the lab; Jack's prothèse is on time.
        await LabOrderAsync("Ross Geller", 36, "Couronne céramo-métallique, teinte A2", -6, -1, 180m, ct);
        await LabOrderAsync("Jack Geller", null, "Prothèse amovible partielle — résine", -4, 8, 320m, ct);
    }

    private async Task LabOrderAsync(
        string patient, int? tooth, string work, int sentDaysAgo, int expectedInDays, decimal cost, CancellationToken ct) =>
        Must(await _seed.SendAsAsync(_monica, new CreateLabWorkOrderCommand
        {
            PatientId = PatientNamed(patient).Id,
            ToothNumber = tooth,
            Prosthetist = "Laboratoire Dentalab",
            WorkDescription = work,
            SentDate = DateTime.SpecifyKind(WorkingDayBefore(sentDaysAgo), DateTimeKind.Utc),
            ExpectedDate = DateTime.SpecifyKind(_today.AddDays(expectedInDays), DateTimeKind.Utc),
            Cost = cost,
            SupplierId = _labSupplierId,
        }, ct), $"envoyer au laboratoire le travail de {patient}");

    // ---- worklists -----------------------------------------------------------------------------------------------

    // Gunther: a canal begun and left unfinished, with no devis — « Suites à planifier » lists it.
    private async Task SuiteToPlanAsync(CancellationToken ct)
    {
        var act = Named("Traitement de canal (dévitalisation)");
        var cost = act.DefaultCost ?? 0m;
        Must(await _seed.SendAsAsync(_chandler, new CreateDentalRecordCommand
        {
            PatientId = PatientNamed("Gunther Centralperk").Id,
            InterventionDate = ClinicClock.ToUtc(WorkingDayBefore(-9) + new TimeSpan(12, 0, 0)),
            DoctorId = _chandlerDoctorId,
            IsAdultTeeth = true,
            Acts = new()
            {
                new() { ProcedureTypeId = act.Id, ProcedureName = act.Name, Cost = cost, UnitCost = cost, ToothNumbers = new() { 11 }, IsUnfinished = true },
            },
            AmountPaid = cost,
            PaymentMethod = "Card",
            Notes = new() { "Pansement provisoire — terminer le traitement canalaire" },
        }, ct), "enregistrer le canal non terminé de Gunther");
    }

    private static string DayKey(DateTime day) => day.ToString("yyyy-MM-dd");
}
