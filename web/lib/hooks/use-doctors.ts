'use client'

import { useMemo } from 'react'
import type { ClinicDto, DoctorDto } from '@/lib/api/clinics'
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
   * <p>The dashboard needs the saved **working hours** to know how full a day is, and this read already carries them.</p>
   */
  clinic: ClinicDto | null
  isLoading: boolean
  error: string | null
  refresh: () => void
}

/**
 * Hook to fetch doctors list and get current user's doctor info
 * Auto-selects the current user's doctor if they are a doctor
 * Derived from the shared clinic status, so it adds no request of its own.
 */
export function useDoctors(): UseDoctorsResult {
  const { status, isLoading, refresh } = useClinicAccess(false)

  const allDoctors = useMemo(() => (status?.hasClinic ? status.doctors ?? [] : []), [status])

  // The practitioner can hold ANY role — in a single-dentist cabinet it is an "admin" with a linked Doctor — so match
  // on the linked user id first (mirrors the backend GetByUserIdAsync), then email/name for records predating the link.
  const currentUserDoctor = useMemo(() => {
    const currentUser = status?.user
    if (!currentUser) return null
    return (
      allDoctors.find((doctor) => {
        if (doctor.userId && doctor.userId === currentUser.id) return true
        if (currentUser.email && doctor.email && doctor.email.toLowerCase() === currentUser.email.toLowerCase()) return true
        return Boolean(currentUser.fullName && doctor.name && doctor.name.toLowerCase() === currentUser.fullName.toLowerCase())
      }) ?? null
    )
  }, [status, allDoctors])

  const doctors = useMemo(() => allDoctors.filter((d) => d.isActive !== false), [allDoctors])

  return {
    doctors,
    allDoctors,
    currentUserDoctor,
    clinic: status?.clinic ?? null,
    isLoading,
    error: null,
    refresh,
  }
}

/**
 * A picker's options: the active roster, plus the one already chosen on this record even if that practitioner has
 * since been retired (I3) — so reopening an old visit or devis still shows who it is on, instead of a blank select.
 */
export function doctorsForPicker(allDoctors: DoctorDto[], keepId?: string | null): DoctorDto[] {
  return allDoctors.filter((d) => d.isActive !== false || (keepId != null && d.id === keepId))
}
