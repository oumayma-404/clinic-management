"use client"

import { useMemo } from "react"
import { useRouter } from "next/navigation"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Users, Flag } from "lucide-react"

// Sample patient data
const patientsData = [
  {
    id: "1",
    name: "John Anderson",
    dateOfBirth: "1985-03-15",
    phone: "(555) 123-4567",
    flagged: true,
    nextAppointment: "2024-01-25 09:00 AM",
  },
  {
    id: "2",
    name: "Emily Roberts",
    dateOfBirth: "1992-07-22",
    phone: "(555) 234-5678",
    flagged: false,
    nextAppointment: "2024-01-26 10:00 AM",
  },
  {
    id: "3",
    name: "Michael Chen",
    dateOfBirth: "1978-11-08",
    phone: "(555) 345-6789",
    flagged: true,
    nextAppointment: "2024-01-25 11:00 AM",
  },
  {
    id: "4",
    name: "Sarah Williams",
    dateOfBirth: "1990-05-30",
    phone: "(555) 456-7890",
    flagged: false,
    nextAppointment: "2024-01-27 02:00 PM",
  },
  {
    id: "5",
    name: "David Brown",
    dateOfBirth: "1983-09-12",
    phone: "(555) 567-8901",
    flagged: false,
    nextAppointment: "2024-01-28 03:30 PM",
  },
  {
    id: "6",
    name: "Jennifer Martinez",
    dateOfBirth: "1995-02-18",
    phone: "(555) 678-9012",
    flagged: true,
    nextAppointment: "2024-01-29 09:00 AM",
  },
  {
    id: "7",
    name: "Robert Taylor",
    dateOfBirth: "1970-12-25",
    phone: "(555) 789-0123",
    flagged: false,
    nextAppointment: "2024-01-30 10:30 AM",
  },
  {
    id: "8",
    name: "Lisa Johnson",
    dateOfBirth: "1988-06-14",
    phone: "(555) 890-1234",
    flagged: true,
    nextAppointment: "2024-01-25 01:00 PM",
  },
]

interface PatientsTableProps {
  searchQuery: string
  showFlaggedOnly: boolean
}

export function PatientsTable({ searchQuery, showFlaggedOnly }: PatientsTableProps) {
  const router = useRouter()

  // Filter patients based on search and flagged status
  const filteredPatients = useMemo(() => {
    return patientsData.filter((patient) => {
      const matchesSearch =
        patient.name.toLowerCase().includes(searchQuery.toLowerCase()) || patient.phone.includes(searchQuery)

      const matchesFlagged = !showFlaggedOnly || patient.flagged

      return matchesSearch && matchesFlagged
    })
  }, [searchQuery, showFlaggedOnly])

  // Format date of birth
  const formatDateOfBirth = (dob: string) => {
    const date = new Date(dob)
    return date.toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" })
  }

  // Calculate age from date of birth
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

  const handleRowClick = (patientId: string) => {
    router.push(`/patients/${patientId}`)
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Users className="h-5 w-5" />
          Patient Records
          <Badge variant="secondary" className="ml-auto">
            {filteredPatients.length} {filteredPatients.length === 1 ? "patient" : "patients"}
          </Badge>
        </CardTitle>
      </CardHeader>
      <CardContent>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Date of Birth</TableHead>
              <TableHead>Phone</TableHead>
              <TableHead>Flags</TableHead>
              <TableHead>Next Appointment</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {filteredPatients.length === 0 ? (
              <TableRow>
                <TableCell colSpan={5} className="h-24 text-center">
                  <p className="text-muted-foreground">No patients found</p>
                </TableCell>
              </TableRow>
            ) : (
              filteredPatients.map((patient) => (
                <TableRow key={patient.id} onClick={() => handleRowClick(patient.id)} className="cursor-pointer">
                  <TableCell className="font-medium">
                    <div>
                      <p className="text-foreground">{patient.name}</p>
                      <p className="text-xs text-muted-foreground">{calculateAge(patient.dateOfBirth)} years old</p>
                    </div>
                  </TableCell>
                  <TableCell className="text-muted-foreground">{formatDateOfBirth(patient.dateOfBirth)}</TableCell>
                  <TableCell className="text-muted-foreground">{patient.phone}</TableCell>
                  <TableCell>
                    {patient.flagged ? (
                      <Badge variant="destructive" className="gap-1">
                        <Flag className="h-3 w-3" />
                        Flagged
                      </Badge>
                    ) : (
                      <span className="text-muted-foreground">-</span>
                    )}
                  </TableCell>
                  <TableCell className="text-muted-foreground">{patient.nextAppointment}</TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  )
}
