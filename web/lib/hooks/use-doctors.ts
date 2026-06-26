'use client'

import { useState, useEffect, useCallback } from 'react'
import { clinicsApi, type DoctorDto } from '@/lib/api/clinics'
import { useClinicAccess } from './use-clinic-access'

export interface UseDoctorsResult {
  doctors: DoctorDto[]
  currentUserDoctor: DoctorDto | null
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
  const [doctors, setDoctors] = useState<DoctorDto[]>([])
  const [currentUserDoctor, setCurrentUserDoctor] = useState<DoctorDto | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const loadDoctors = useCallback(async () => {
    if (clinicLoading) {
      return
    }

    if (!status?.hasClinic || !status.doctors) {
      setDoctors([])
      setCurrentUserDoctor(null)
      setIsLoading(false)
      return
    }

    try {
      setIsLoading(true)
      setError(null)

      const doctorsList = status.doctors || []
      setDoctors(doctorsList)

      // Find current user's doctor if they are a doctor
      if (status.role === 'doctor' && status.user) {
        // Try to find doctor by matching user info
        // The doctor's UserId should match the user's id (Auth0 sub)
        // For now, we'll match by email or name as a fallback
        const userEmail = status.user.email
        const userFullName = status.user.fullName

        const userDoctor = doctorsList.find(doctor => {
          // Try to match by email first
          if (userEmail && doctor.email && doctor.email.toLowerCase() === userEmail.toLowerCase()) {
            return true
          }
          // Try to match by name
          if (userFullName && doctor.name && doctor.name.toLowerCase() === userFullName.toLowerCase()) {
            return true
          }
          return false
        })

        if (userDoctor) {
          setCurrentUserDoctor(userDoctor)
        } else {
          setCurrentUserDoctor(null)
        }
      } else {
        setCurrentUserDoctor(null)
      }
    } catch (err: any) {
      console.error('Error loading doctors:', err)
      setError(err.message || 'Failed to load doctors')
      setDoctors([])
      setCurrentUserDoctor(null)
    } finally {
      setIsLoading(false)
    }
  }, [status, clinicLoading])

  useEffect(() => {
    loadDoctors()
  }, [loadDoctors])

  return {
    doctors,
    currentUserDoctor,
    isLoading,
    error,
    refresh: loadDoctors,
  }
}



