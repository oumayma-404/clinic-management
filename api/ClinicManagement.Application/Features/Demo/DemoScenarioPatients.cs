using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;

namespace ClinicManagement.Application.Features.Demo;

/// <summary>One fictional patient. <see cref="AgeYears"/> is relative to the build day, so the cabinet never ages.</summary>
public sealed record DemoPatientSpec(
    string FirstName,
    string LastName,
    string Gender,
    int AgeYears,
    string Dentition = "Adult",
    string? Allergies = null,
    string? MedicalHistory = null,
    string? Medications = null,
    string? ConsultationReason = null,
    string? ImportantNotes = null,
    string Tobacco = "NonSmoker")
{
    public bool IsChild => Dentition != "Adult";

    public string FullName => $"{FirstName} {LastName}";
}

/// <summary>
/// The cabinet's patients — « Friends » names, every file filled (contact, address, health, antécédents) so any
/// patient a visitor opens reads like a real one. Allergies and maladies are given only to some: any value there
/// raises the red « Alertes médicales » panel, so writing « aucune » would put an alert on every file.
/// </summary>
public static class DemoScenarioPatients
{
    public const string Male = "Male";
    public const string Female = "Female";

    public static readonly IReadOnlyList<DemoPatientSpec> All = new DemoPatientSpec[]
    {
        // Ross Geller as in the « Premiers pas » videos.
        new("Ross", "Geller", Male, 34, Allergies: "Pénicilline", MedicalHistory: "Asthme",
            Medications: "Ventoline en cas de crise", ConsultationReason: "Douleur molaire en bas à gauche",
            ImportantNotes: "Allergie à la pénicilline — prescrire de la clindamycine"),
        new("Phoebe", "Buffay", Female, 33, ConsultationReason: "Contrôle annuel"),
        new("Joey", "Tribbiani", Male, 32, ConsultationReason: "Blanchiment avant un tournage", Tobacco: "FormerSmoker"),
        new("Ben", "Geller", Male, 8, Dentition: "Mixed", ConsultationReason: "Suivi orthodontique"),
        new("Emma", "Geller", Female, 4, Dentition: "Child", ConsultationReason: "Première visite"),
        new("Jack", "Geller", Male, 66, MedicalHistory: "Hypertension", Medications: "Amlodipine 5 mg",
            ConsultationReason: "Prothèse amovible qui blesse", Tobacco: "FormerSmoker"),
        new("Judy", "Geller", Female, 63, MedicalHistory: "Diabète de type 2", Medications: "Metformine 850 mg",
            ConsultationReason: "Gencives qui saignent"),
        new("Mike", "Hannigan", Male, 36, ConsultationReason: "Sensibilité au froid"),
        new("Janice", "Hosenstein", Female, 35, ConsultationReason: "Détartrage", Tobacco: "Smoker"),
        new("Gunther", "Centralperk", Male, 41, ConsultationReason: "Dent cassée", Tobacco: "Smoker"),
        new("Carol", "Willick", Female, 37, Allergies: "Latex", ConsultationReason: "Contrôle annuel",
            ImportantNotes: "Allergie au latex — gants nitrile"),
        new("Susan", "Bunch", Female, 38, Medications: "Lévothyrox 75 µg", MedicalHistory: "Hypothyroïdie",
            ConsultationReason: "Bilan dentaire"),
        new("Richard", "Burke", Male, 58, MedicalHistory: "Anticoagulant (AVK)", Medications: "Warfarine",
            ConsultationReason: "Dent qui bouge", ImportantNotes: "Sous AVK — INR avant tout acte sanglant",
            Tobacco: "FormerSmoker"),
        new("Emily", "Waltham", Female, 31, ConsultationReason: "Esthétique du sourire"),
        new("Frank", "Buffay", Male, 29, ConsultationReason: "Dent de sagesse"),
        new("Alice", "Knight", Female, 44, Allergies: "Ibuprofène", ConsultationReason: "Douleur au chaud"),
        new("Ursula", "Buffay", Female, 33, ConsultationReason: "Contrôle annuel"),
        new("Pete", "Becker", Male, 39, ConsultationReason: "Implant à discuter"),
        new("Tag", "Jones", Male, 26, ConsultationReason: "Détartrage"),
        new("Paolo", "Rossi", Male, 35, ConsultationReason: "Tache sur une incisive", Tobacco: "Smoker"),
        new("Charlie", "Wheeler", Female, 34, MedicalHistory: "Migraine", Medications: "Triptan si crise",
            ConsultationReason: "Grincement des dents la nuit"),
        new("Tim", "Burke", Male, 27, ConsultationReason: "Bilan dentaire"),
        new("Erica", "Ford", Female, 30, ConsultationReason: "Grossesse — contrôle",
            ImportantNotes: "Enceinte (5e mois) — pas de radio sans protection"),
        new("Estelle", "Leonard", Female, 61, MedicalHistory: "Ostéoporose", Medications: "Calcium + vitamine D",
            ConsultationReason: "Prothèse à refaire"),
        new("Leonard", "Green", Male, 64, MedicalHistory: "Cholestérol", Medications: "Atorvastatine 20 mg",
            ConsultationReason: "Bridge à refaire"),
        new("Sandra", "Green", Female, 60, Allergies: "Iode", ConsultationReason: "Contrôle annuel"),
        new("Amy", "Green", Female, 28, ConsultationReason: "Blanchiment"),
        new("Jill", "Green", Female, 25, ConsultationReason: "Facettes"),
        new("Eddie", "Menuek", Male, 40, ConsultationReason: "Carie sur une prémolaire"),
        new("Mark", "Robinson", Male, 33, ConsultationReason: "Saignement des gencives"),
        new("Barry", "Farber", Male, 36, Allergies: "Aspirine", ConsultationReason: "Douleur au froid"),
        new("Mindy", "Hunter", Female, 34, ConsultationReason: "Contrôle annuel"),
        new("Joshua", "Bergin", Male, 32, ConsultationReason: "Dent cassée"),
        new("Will", "Colbert", Male, 35, ConsultationReason: "Détartrage"),
        new("Elizabeth", "Stevens", Female, 22, ConsultationReason: "Appareil dentaire"),
        new("Paul", "Stevens", Male, 59, MedicalHistory: "Cardiopathie", Medications: "Bisoprolol 5 mg",
            ConsultationReason: "Abcès", Tobacco: "FormerSmoker"),
        new("Kathy", "Miller", Female, 30, ConsultationReason: "Esthétique du sourire"),
        new("Gary", "Hollis", Male, 42, ConsultationReason: "Bilan dentaire", Tobacco: "Smoker"),
        new("Mona", "Peters", Female, 37, Allergies: "Pollen", ConsultationReason: "Contrôle annuel"),
        new("Danny", "Moore", Male, 33, ConsultationReason: "Sensibilité au froid"),
        new("Marcel", "Geller", Male, 12, Dentition: "Mixed", ConsultationReason: "Scellement de sillons"),
    };

    private static readonly string[] MobilePrefixes = { "20", "22", "25", "50", "52", "55", "93", "97", "98" };

    private static readonly (string Street, string City, string State, string Zip)[] Addresses =
    {
        ("12 rue de Marseille", "Tunis", "Tunis", "1000"),
        ("5 avenue Habib Bourguiba", "La Marsa", "Tunis", "2070"),
        ("27 rue Ibn Khaldoun", "Ariana", "Ariana", "2080"),
        ("8 rue du Lac Léman", "Les Berges du Lac", "Tunis", "1053"),
        ("3 rue des Oliviers", "El Menzah", "Tunis", "1004"),
        ("14 avenue de Carthage", "Carthage", "Tunis", "2016"),
        ("21 rue de Palestine", "Ben Arous", "Ben Arous", "2013"),
        ("9 rue Hannibal", "La Soukra", "Ariana", "2036"),
        ("16 rue des Jasmins", "Manouba", "Manouba", "2010"),
        ("2 avenue de la République", "Ezzahra", "Ben Arous", "2034"),
    };

    private static readonly string[] ReferredBy =
    {
        "Bouche-à-oreille", "Dr Chandler Bing", "Recherche internet", "Un patient du cabinet", "Médecin traitant",
    };

    private static readonly string[] DeskNotes =
    {
        "Préfère les rendez-vous du matin.", "Anxieux — expliquer chaque geste avant de commencer.",
        "Disponible surtout le samedi.", "Préfère être appelé plutôt qu'un message.",
        "Très régulier aux contrôles.", "Règle en général par chèque.", "Souhaite un devis avant tout soin.",
        "Travaille à côté du cabinet — peut venir au dernier moment.",
    };

    private static readonly string[] SurgicalHistory =
    {
        "Appendicectomie", "Amygdalectomie dans l'enfance", "Fracture du poignet droit", "Opération du ménisque",
        "Hospitalisation pour une pneumonie", "Ablation de la vésicule biliaire", "Chirurgie de la cloison nasale",
    };

    private static readonly string[] ChildHistory = { "Varicelle", "Otites à répétition", "Vaccinations à jour" };

    private static readonly (string Relationship, string Condition)[] FamilyHistory =
    {
        ("Père", "Diabète de type 2"), ("Mère", "Hypertension"), ("Père", "Maladie cardiaque"),
        ("Mère", "Maladie parodontale"), ("Grand-père", "Diabète"), ("Mère", "Ostéoporose"), ("Père", "Asthme"),
    };

    /// <summary>A Tunisian mobile written as a run of zeros, so it reads as made up.</summary>
    public static string PhoneFor(int index) => $"{MobilePrefixes[index % MobilePrefixes.Length]} 000 {100 + index:000}";

    /// <summary>
    /// The full patient file for one spec. Every value is fictional; the e-mail uses the reserved <c>example.com</c>
    /// domain so no address can belong to anybody.
    /// </summary>
    public static CreatePatientCommand CommandFor(DemoPatientSpec spec, int index, DateTime birth, DateTime today)
    {
        var address = Addresses[index % Addresses.Length];
        var contact = All[(index + 7) % All.Count];
        var parent = All[0];
        var family = FamilyHistory[index % FamilyHistory.Length];
        return new CreatePatientCommand
        {
            FirstName = spec.FirstName,
            LastName = spec.LastName,
            Gender = spec.Gender,
            DateOfBirth = DateTime.SpecifyKind(birth, DateTimeKind.Utc),
            Dentition = spec.Dentition,
            PhoneNumber = PhoneFor(index),
            PhoneRegion = "TN",
            Email = $"{spec.FirstName}.{spec.LastName}@example.com".ToLowerInvariant(),
            Address = new AddressDto
            {
                Street = address.Street, City = address.City, State = address.State, ZipCode = address.Zip, Country = "Tunisie",
            },
            EmergencyContactName = spec.IsChild ? $"{parent.FullName} (père)" : contact.FullName,
            EmergencyContactPhone = PhoneFor(spec.IsChild ? 0 : (index + 7) % All.Count),
            ReferredBy = ReferredBy[index % ReferredBy.Length],
            Allergies = spec.Allergies,
            MedicalHistory = spec.MedicalHistory,
            Medications = spec.Medications,
            ConsultationReason = spec.ConsultationReason,
            ImportantNotes = spec.ImportantNotes,
            Notes = spec.IsChild ? "Toujours accompagné d'un parent." : DeskNotes[index % DeskNotes.Length],
            TobaccoUse = spec.IsChild
                ? null
                : spec.Tobacco == "Smoker"
                    ? new TobaccoUseDto { Status = "Smoker", PerDay = 10, Unit = "Cigarettes" }
                    : new TobaccoUseDto { Status = spec.Tobacco },
            ReminderConsent = index % 9 == 8 ? "Refused" : "Granted",
            MedicalHistoryEntries = new List<MedicalHistoryEntryDto>
            {
                new()
                {
                    Description = spec.IsChild ? ChildHistory[index % ChildHistory.Length] : SurgicalHistory[index % SurgicalHistory.Length],
                    Date = DateTime.SpecifyKind(today.AddYears(-(spec.IsChild ? 2 : 3 + index % 12)), DateTimeKind.Utc),
                },
            },
            FamilyHistoryEntries = new List<FamilyHistoryEntryDto>
            {
                new() { Relationship = family.Relationship, Condition = family.Condition },
            },
            AllowDuplicate = true,
        };
    }
}
