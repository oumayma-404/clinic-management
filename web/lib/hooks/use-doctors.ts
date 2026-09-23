'use client'

import { useState, useEffect, useCallback, useMemo } from 'react'
import { clinicsApi, type ClinicDto, type DoctorDto } from '@/lib/api/clinics'
import { useClinicAccess } from './use-clinic-access'

export interface UseDoctorsResult {
  /** The roster — practitioners still active. What a picker offers. */
  doctors: DoctorDto[]
  /**
   * Every practitioner the clinic has had, retired ones included (I3) — for resolving a name on history and for
   * filters over past work. Retiring used to delete the row and take « who did it » off every record.
   */
  allDoctors: DoctorDto[]
  currentUserDoctor: DoctorDto | null
  /**
   * The clinic this status read already carried.
   *
   * <p>Returned rather than thrown away because `useClinicAccess` has no cache — every caller is another request
   * — and the dashboard needs the saved **working hours** to know how full a day is. A dedicated hook for that
   * would be a third fetch of a payload this one has already paid for.</p>
   */
  clinic: ClinicDto | null
  isLoading: boolean
  error: string | null
  refresh: () => void
}

/**
 * Hook to fetch doctors list and get current user's doctor info
 * Auto-selects the current user's doctor if they are a doctor
 */
export function useDoctors(): UseDoctorsResult {
  const { status, isLoading: clinicLoading } = useClinicAccess(false)
  const [allDoctors, setAllDoctors] = useState<DoctorDto[]>([])
  const [currentUserDoctor, setCurrentUserDoctor] = useState<DoctorDto | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const loadDoctors = useCallback(async () => {
    if (clinicLoading) {
      return
    }

    if (!status?.hasClinic || !status.doctors) {
      setAllDoctors([])
      setCurrentUserDoctor(null)
      setIsLoading(false)
      return
    }

    try {
      setIsLoading(true)
      setError(null)

      const doctorsList = status.doctors || []
      setAllDoctors(doctorsList)

      // Resolve the current user's linked doctor (if any). The practitioner can hold ANY role — in a
      // single-dentist cabinet the practitioner is an "admin" with a linked Doctor — so match on the
      // authoritative linked user id first (mirrors the backend GetByUserIdAsync), then fall back to
      // email/name for legacy records created before the link existed.
      const currentUser = status.user
      if (currentUser) {
        const userDoctor = doctorsList.find(doctor => {
          // Authoritative: the doctor is linked to this user id.
          if (doctor.userId && doctor.userId === currentUser.id) {
            return true
          }
          // Fallback: match by email.
          if (currentUser.email && doctor.email && doctor.email.toLowerCase() === currentUser.email.toLowerCase()) {
            return true
          }
          // Fallback: match by name.
          if (currentUser.fullName && doctor.name && doctor.name.toLowerCase() === currentUser.fullName.toLowerCase()) {
            return true
          }
          return false
        })

        setCurrentUserDoctor(userDoctor ?? null)
      } else {
        setCurrentUserDoctor(null)
      }
    } catch (err: any) {
      console.error('Error loading doctors:', err)
      setError(err.message || 'Échec du chargement des médecins')
      setAllDoctors([])
      setCurrentUserDoctor(null)
    } finally {
      setIsLoading(false)
    }
  }, [status, clinicLoading])

  useEffect(() => {
    loadDoctors()
  }, [loadDoctors])

  const doctors = useMemo(() => allDoctors.filter((d) => d.isActive !== false), [allDoctors])

  return {
    doctors,
    allDoctors,
    currentUserDoctor,
    clinic: status?.clinic ?? null,
    isLoading,
    error,
    refresh: loadDoctors,
  }
}

/**
 * A picker's options: the active roster, plus the one already chosen on this record even if that practitioner has
 * since been retired (I3) — so reopening an old visit or devis still shows who it is on, instead of a blank select.
 */
export function doctorsForPicker(allDoctors: DoctorDto[], keepId?: string | null): DoctorDto[] {
  return allDoctors.filter((d) => d.isActive !== false || (keepId != null && d.id === keepId))
}
