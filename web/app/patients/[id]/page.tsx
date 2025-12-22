"use client"

import { useParams, useRouter } from "next/navigation"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { Separator } from "@/components/ui/separator"
import {
  ArrowLeft,
  Flag,
  Calendar,
  FileText,
  Sparkles,
  Download,
  Eye,
  User,
  Activity,
  Heart,
  Stethoscope,
  FileCheck,
  CreditCard,
  Bell,
} from "lucide-react"

const getPatientData = (id: string) => {
  const patients: Record<string, any> = {
    "1": {
      id: "1",
      // Personal Info
      name: "John Anderson",
      dateOfBirth: "1985-03-15",
      gender: "Male",
      phone: "(555) 123-4567",
      email: "john.anderson@email.com",
      address: "123 Main St, Springfield, IL 62701",
      flagged: true,

      // Medical Info
      chronicDiseases: ["Hypertension (High Blood Pressure)", "Type 2 Diabetes"],
      pastSurgeries: [
        { procedure: "Appendectomy", date: "2015-06-10", hospital: "Springfield General" },
        { procedure: "Knee Arthroscopy", date: "2019-03-22", hospital: "Memorial Hospital" },
      ],
      familyHistory: ["Father - Heart Disease", "Mother - Diabetes Type 2"],
      proceduresWithClinic: [
        {
          type: "Consultation",
          date: "2024-01-10",
          doctor: "Dr. Sarah Johnson",
          notes: "Initial diabetes consultation",
        },
        { type: "Follow-up", date: "2024-01-18", doctor: "Dr. Michael Chen", notes: "Blood pressure monitoring" },
        { type: "Lab Work", date: "2024-01-15", doctor: "Dr. Sarah Johnson", notes: "HbA1c and lipid panel" },
      ],
      allergies: ["Penicillin", "Shellfish"],

      // Administrative
      insuranceProvider: "Blue Cross Blue Shield",
      insuranceNumber: "BCBS-123456789",
      policyHolder: "John Anderson",

      // Clinic Specific
      clinicNotes: [
        {
          date: "2024-01-18",
          author: "Dr. Michael Chen",
          note: "Patient showing good progress with medication adherence. Blood pressure readings have improved.",
        },
        {
          date: "2024-01-10",
          author: "Dr. Sarah Johnson",
          note: "Patient reports occasional dizziness. Adjusted medication dosage.",
        },
      ],
      observations: [
        { date: "2024-01-18", observation: "Blood Pressure: 128/82 mmHg" },
        { date: "2024-01-18", observation: "Weight: 185 lbs" },
        { date: "2024-01-15", observation: "HbA1c: 6.8%" },
      ],
      aiSummary:
        "Patient has a history of hypertension and Type 2 diabetes, currently well-managed with medication. Shows good adherence to treatment plan. Allergic to Penicillin and shellfish - use alternative medications. Family history indicates cardiovascular risk. Recent lab work shows improvement in glucose levels. Recommended continued monitoring every 3 months.",

      // Files
      files: [
        { id: 1, name: "Lab Results - Blood Test.pdf", date: "2024-01-15", size: "245 KB", type: "Lab Results" },
        { id: 2, name: "Prescription History.pdf", date: "2024-01-05", size: "89 KB", type: "Prescription" },
        { id: 3, name: "Insurance Card.pdf", date: "2024-01-01", size: "156 KB", type: "Insurance" },
      ],

      // Appointments
      appointments: [
        {
          id: 1,
          date: "2024-01-25 09:00 AM",
          doctor: "Dr. Sarah Johnson",
          type: "Check-up",
          status: "scheduled",
          reason: "Quarterly diabetes monitoring",
        },
        {
          id: 2,
          date: "2024-01-18 10:00 AM",
          doctor: "Dr. Michael Chen",
          type: "Follow-up",
          status: "completed",
          reason: "Blood pressure check",
        },
        {
          id: 3,
          date: "2024-01-10 02:00 PM",
          doctor: "Dr. Sarah Johnson",
          type: "Consultation",
          status: "completed",
          reason: "Initial diabetes consultation",
        },
      ],

      // Notifications & Reminders
      reminders: [
        {
          id: 1,
          type: "appointment",
          message: "Upcoming appointment on Jan 25, 2024",
          date: "2024-01-25",
          priority: "high",
        },
        { id: 2, type: "medication", message: "Prescription refill due", date: "2024-01-30", priority: "medium" },
        { id: 3, type: "lab", message: "Schedule annual blood work", date: "2024-02-15", priority: "low" },
      ],
    },
  }

  return patients[id] || null
}

const calculateAge = (dob: string) => {
  const birthDate = new Date(dob)
  const today = new Date()
  let age = today.getFullYear() - birthDate.getFullYear()
  const monthDiff = today.getMonth() - birthDate.getMonth()
  if (monthDiff < 0 || (monthDiff === 0 && today.getDate() < birthDate.getDate())) {
    age--
  }
  return age
}

const formatDate = (dateString: string) => {
  const date = new Date(dateString)
  return date.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" })
}

export default function PatientDetailsPage() {
  const params = useParams()
  const router = useRouter()
  const patientId = params.id as string
  const patient = getPatientData(patientId)

  if (!patient) {
    return (
      <div className="flex h-screen bg-background">
        <DashboardSidebar />
        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />
          <main className="flex flex-1 items-center justify-center">
            <div className="text-center">
              <h2 className="text-2xl font-semibold text-foreground">Patient Not Found</h2>
              <p className="mt-2 text-muted-foreground">The patient you are looking for does not exist.</p>
              <Button onClick={() => router.push("/patients")} className="mt-4">
                Back to Patients
              </Button>
            </div>
          </main>
        </div>
      </div>
    )
  }

  return (
    <div className="flex h-screen bg-background">
      <DashboardSidebar />

      <div className="flex flex-1 flex-col overflow-hidden">
        <DashboardHeader />

        <main className="flex-1 overflow-y-auto p-6">
          <div className="mx-auto max-w-7xl space-y-6">
            {/* Back Button */}
            <Button variant="ghost" onClick={() => router.push("/patients")} className="gap-2">
              <ArrowLeft className="h-4 w-4" />
              Back to Patients
            </Button>

            <div className="flex items-center justify-between">
              <div className="flex items-center gap-3">
                <h1 className="text-3xl font-semibold text-foreground">{patient.name}</h1>
                {patient.flagged && (
                  <Badge variant="destructive" className="gap-1">
                    <Flag className="h-3 w-3" />
                    Flagged
                  </Badge>
                )}
              </div>
              <div className="flex gap-2">
                <Button variant="outline">Edit Patient</Button>
                <Button>Schedule Appointment</Button>
              </div>
            </div>

            <Card className="border-blue-200 bg-blue-50/50 dark:border-blue-900 dark:bg-blue-950/20">
              <CardHeader>
                <CardTitle className="flex items-center gap-2 text-blue-700 dark:text-blue-400">
                  <Sparkles className="h-5 w-5" />
                  AI-Generated Patient Summary
                </CardTitle>
                <CardDescription>Automatically generated overview based on patient records</CardDescription>
              </CardHeader>
              <CardContent>
                <p className="text-sm leading-relaxed text-foreground">{patient.aiSummary}</p>
                <div className="mt-4 flex items-center gap-2 text-xs text-muted-foreground">
                  <Sparkles className="h-3 w-3" />
                  <span>Last updated: {formatDate(new Date().toISOString())}</span>
                </div>
              </CardContent>
            </Card>

            <div className="grid gap-6 lg:grid-cols-3">
              {/* Personal Information */}
              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <User className="h-5 w-5 text-muted-foreground" />
                    Personal Information
                  </CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Full Name</p>
                    <p className="text-sm text-foreground">{patient.name}</p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Date of Birth</p>
                    <p className="text-sm text-foreground">
                      {formatDate(patient.dateOfBirth)} ({calculateAge(patient.dateOfBirth)} years old)
                    </p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Gender</p>
                    <p className="text-sm text-foreground">{patient.gender}</p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Mobile</p>
                    <p className="text-sm text-foreground">{patient.phone}</p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Email</p>
                    <p className="text-sm text-foreground">{patient.email || "Not provided"}</p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Address</p>
                    <p className="text-sm text-foreground">{patient.address}</p>
                  </div>
                </CardContent>
              </Card>

              {/* Medical Information */}
              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <Activity className="h-5 w-5 text-muted-foreground" />
                    Medical Information
                  </CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Chronic Diseases/Conditions</p>
                    {patient.chronicDiseases.length > 0 ? (
                      <ul className="mt-1 space-y-1">
                        {patient.chronicDiseases.map((disease: string, index: number) => (
                          <li key={index} className="text-sm text-foreground">
                            • {disease}
                          </li>
                        ))}
                      </ul>
                    ) : (
                      <p className="text-sm text-muted-foreground">None reported</p>
                    )}
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Allergies</p>
                    {patient.allergies && patient.allergies.length > 0 ? (
                      <div className="mt-1 flex flex-wrap gap-1">
                        {patient.allergies.map((allergy: string, index: number) => (
                          <Badge key={index} variant="destructive" className="text-xs">
                            {allergy}
                          </Badge>
                        ))}
                      </div>
                    ) : (
                      <p className="text-sm text-muted-foreground">None reported</p>
                    )}
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Past Surgeries</p>
                    {patient.pastSurgeries.length > 0 ? (
                      <ul className="mt-1 space-y-2">
                        {patient.pastSurgeries.map((surgery: any, index: number) => (
                          <li key={index} className="text-sm">
                            <p className="font-medium text-foreground">{surgery.procedure}</p>
                            <p className="text-xs text-muted-foreground">
                              {formatDate(surgery.date)} - {surgery.hospital}
                            </p>
                          </li>
                        ))}
                      </ul>
                    ) : (
                      <p className="text-sm text-muted-foreground">None reported</p>
                    )}
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Family Medical History</p>
                    {patient.familyHistory.length > 0 ? (
                      <ul className="mt-1 space-y-1">
                        {patient.familyHistory.map((history: string, index: number) => (
                          <li key={index} className="text-sm text-foreground">
                            • {history}
                          </li>
                        ))}
                      </ul>
                    ) : (
                      <p className="text-sm text-muted-foreground">None reported</p>
                    )}
                  </div>
                </CardContent>
              </Card>

              {/* Administrative Information */}
              <Card>
                <CardHeader>
                  <CardTitle className="flex items-center gap-2 text-base">
                    <CreditCard className="h-5 w-5 text-muted-foreground" />
                    Administrative Information
                  </CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Insurance Provider</p>
                    <p className="text-sm text-foreground">{patient.insuranceProvider}</p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Insurance Number</p>
                    <p className="font-mono text-sm text-foreground">{patient.insuranceNumber}</p>
                  </div>
                  <Separator />
                  <div>
                    <p className="text-xs font-medium text-muted-foreground">Policy Holder</p>
                    <p className="text-sm text-foreground">{patient.policyHolder}</p>
                  </div>
                </CardContent>
              </Card>
            </div>

            <Tabs defaultValue="procedures" className="space-y-4">
              <TabsList className="grid w-full grid-cols-6">
                <TabsTrigger value="procedures" className="gap-2">
                  <Stethoscope className="h-4 w-4" />
                  Procedures
                </TabsTrigger>
                <TabsTrigger value="notes" className="gap-2">
                  <FileCheck className="h-4 w-4" />
                  Notes
                </TabsTrigger>
                <TabsTrigger value="appointments" className="gap-2">
                  <Calendar className="h-4 w-4" />
                  Appointments
                </TabsTrigger>
                <TabsTrigger value="files" className="gap-2">
                  <FileText className="h-4 w-4" />
                  Files
                </TabsTrigger>
                <TabsTrigger value="observations" className="gap-2">
                  <Activity className="h-4 w-4" />
                  Observations
                </TabsTrigger>
                <TabsTrigger value="reminders" className="gap-2">
                  <Bell className="h-4 w-4" />
                  Reminders
                </TabsTrigger>
              </TabsList>

              {/* Procedures Tab */}
              <TabsContent value="procedures">
                <Card>
                  <CardHeader>
                    <CardTitle>Procedures Done at Our Clinic</CardTitle>
                    <CardDescription>Complete history of all procedures and consultations</CardDescription>
                  </CardHeader>
                  <CardContent>
                    {patient.proceduresWithClinic.length === 0 ? (
                      <p className="text-center text-muted-foreground">No procedures recorded</p>
                    ) : (
                      <div className="space-y-4">
                        {patient.proceduresWithClinic.map((procedure: any, index: number) => (
                          <div key={index}>
                            <div className="flex items-start gap-4">
                              <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-blue-100 dark:bg-blue-950">
                                <Stethoscope className="h-5 w-5 text-blue-700 dark:text-blue-400" />
                              </div>
                              <div className="flex-1">
                                <div className="flex items-start justify-between">
                                  <div>
                                    <p className="font-medium text-foreground">{procedure.type}</p>
                                    <p className="text-sm text-muted-foreground">
                                      {formatDate(procedure.date)} • {procedure.doctor}
                                    </p>
                                    {procedure.notes && (
                                      <p className="mt-1 text-sm text-foreground">{procedure.notes}</p>
                                    )}
                                  </div>
                                </div>
                              </div>
                            </div>
                            {index < patient.proceduresWithClinic.length - 1 && <Separator className="mt-4" />}
                          </div>
                        ))}
                      </div>
                    )}
                  </CardContent>
                </Card>
              </TabsContent>

              {/* Notes Tab */}
              <TabsContent value="notes">
                <Card>
                  <CardHeader>
                    <CardTitle>Clinical Notes & Observations</CardTitle>
                    <CardDescription>Doctor notes and clinical observations</CardDescription>
                  </CardHeader>
                  <CardContent>
                    <div className="space-y-6">
                      <div>
                        <h3 className="mb-3 text-sm font-medium text-foreground">Clinical Notes</h3>
                        {patient.clinicNotes.length === 0 ? (
                          <p className="text-center text-muted-foreground">No notes recorded</p>
                        ) : (
                          <div className="space-y-3">
                            {patient.clinicNotes.map((note: any, index: number) => (
                              <div key={index} className="rounded-lg border bg-card p-4">
                                <div className="mb-2 flex items-center justify-between">
                                  <p className="text-xs font-medium text-muted-foreground">{note.author}</p>
                                  <p className="text-xs text-muted-foreground">{formatDate(note.date)}</p>
                                </div>
                                <p className="text-sm text-foreground">{note.note}</p>
                              </div>
                            ))}
                          </div>
                        )}
                      </div>
                    </div>
                  </CardContent>
                </Card>
              </TabsContent>

              {/* Appointments Tab */}
              <TabsContent value="appointments">
                <Card>
                  <CardHeader>
                    <CardTitle>Appointment History</CardTitle>
                    <CardDescription>Complete history of all appointments</CardDescription>
                  </CardHeader>
                  <CardContent>
                    {patient.appointments.length === 0 ? (
                      <p className="text-center text-muted-foreground">No appointments found</p>
                    ) : (
                      <div className="space-y-4">
                        {patient.appointments.map((appointment: any, index: number) => (
                          <div key={appointment.id}>
                            <div className="flex items-start justify-between">
                              <div className="flex items-start gap-4">
                                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-accent">
                                  <Calendar className="h-5 w-5 text-accent-foreground" />
                                </div>
                                <div>
                                  <p className="font-medium text-foreground">{appointment.type}</p>
                                  <p className="text-sm text-muted-foreground">{appointment.date}</p>
                                  <p className="text-xs text-muted-foreground">{appointment.doctor}</p>
                                  {appointment.reason && (
                                    <p className="mt-1 text-xs text-muted-foreground italic">
                                      Reason: {appointment.reason}
                                    </p>
                                  )}
                                </div>
                              </div>
                              <Badge variant={appointment.status === "scheduled" ? "default" : "secondary"}>
                                {appointment.status}
                              </Badge>
                            </div>
                            {index < patient.appointments.length - 1 && <Separator className="mt-4" />}
                          </div>
                        ))}
                      </div>
                    )}
                  </CardContent>
                </Card>
              </TabsContent>

              {/* Files Tab */}
              <TabsContent value="files">
                <Card>
                  <CardHeader>
                    <CardTitle>Uploaded Documents</CardTitle>
                    <CardDescription>Medical records, lab results, and other documents</CardDescription>
                  </CardHeader>
                  <CardContent>
                    {patient.files.length === 0 ? (
                      <p className="text-center text-muted-foreground">No files uploaded</p>
                    ) : (
                      <div className="space-y-2">
                        {patient.files.map((file: any) => (
                          <div key={file.id}>
                            <div className="flex items-center justify-between rounded-lg border p-4 hover:bg-accent">
                              <div className="flex items-center gap-3">
                                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-accent">
                                  <FileText className="h-5 w-5 text-accent-foreground" />
                                </div>
                                <div>
                                  <p className="text-sm font-medium text-foreground">{file.name}</p>
                                  <p className="text-xs text-muted-foreground">
                                    {file.type} • {file.date} • {file.size}
                                  </p>
                                </div>
                              </div>
                              <div className="flex gap-2">
                                <Button variant="ghost" size="sm" className="h-8 gap-2">
                                  <Eye className="h-4 w-4" />
                                  View
                                </Button>
                                <Button variant="ghost" size="sm" className="h-8 gap-2">
                                  <Download className="h-4 w-4" />
                                  Download
                                </Button>
                              </div>
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
                  </CardContent>
                </Card>
              </TabsContent>

              {/* Observations Tab */}
              <TabsContent value="observations">
                <Card>
                  <CardHeader>
                    <CardTitle>Clinical Observations</CardTitle>
                    <CardDescription>Vital signs and measurements taken during visits</CardDescription>
                  </CardHeader>
                  <CardContent>
                    {patient.observations.length === 0 ? (
                      <p className="text-center text-muted-foreground">No observations recorded</p>
                    ) : (
                      <div className="space-y-3">
                        {patient.observations.map((obs: any, index: number) => (
                          <div key={index} className="flex items-start justify-between rounded-lg border p-3">
                            <div className="flex items-start gap-3">
                              <Heart className="mt-0.5 h-5 w-5 text-red-500" />
                              <div>
                                <p className="text-sm font-medium text-foreground">{obs.observation}</p>
                                <p className="text-xs text-muted-foreground">{formatDate(obs.date)}</p>
                              </div>
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
                  </CardContent>
                </Card>
              </TabsContent>

              {/* Reminders Tab */}
              <TabsContent value="reminders">
                <Card>
                  <CardHeader>
                    <CardTitle>Notifications & Reminders</CardTitle>
                    <CardDescription>Important notifications and upcoming reminders</CardDescription>
                  </CardHeader>
                  <CardContent>
                    {patient.reminders.length === 0 ? (
                      <p className="text-center text-muted-foreground">No reminders set</p>
                    ) : (
                      <div className="space-y-3">
                        {patient.reminders.map((reminder: any) => (
                          <div
                            key={reminder.id}
                            className={`rounded-lg border p-4 ${
                              reminder.priority === "high"
                                ? "border-red-200 bg-red-50 dark:border-red-900 dark:bg-red-950/20"
                                : reminder.priority === "medium"
                                  ? "border-amber-200 bg-amber-50 dark:border-amber-900 dark:bg-amber-950/20"
                                  : "border-gray-200 bg-gray-50 dark:border-gray-800 dark:bg-gray-900/20"
                            }`}
                          >
                            <div className="flex items-start gap-3">
                              <Bell
                                className={`mt-0.5 h-5 w-5 ${
                                  reminder.priority === "high"
                                    ? "text-red-600 dark:text-red-400"
                                    : reminder.priority === "medium"
                                      ? "text-amber-600 dark:text-amber-400"
                                      : "text-gray-600 dark:text-gray-400"
                                }`}
                              />
                              <div className="flex-1">
                                <p className="text-sm font-medium text-foreground">{reminder.message}</p>
                                <p className="mt-1 text-xs text-muted-foreground">{formatDate(reminder.date)}</p>
                              </div>
                              <Badge variant={reminder.priority === "high" ? "destructive" : "secondary"}>
                                {reminder.priority}
                              </Badge>
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
                  </CardContent>
                </Card>
              </TabsContent>
            </Tabs>
          </div>
        </main>
      </div>
    </div>
  )
}
