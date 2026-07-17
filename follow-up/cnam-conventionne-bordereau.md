# Conventionné pathway: bordereau + télétransmission

> **Type:** enhancement
> **Priority:** low
> **Created:** 2026-07-17
> **Feature:** cnam-bulletin-soins

## Summary
The entire CNAM bulletin work targets the **filière privée** (système de remboursement): the patient pays, receives the BS1, and files it with CNAM themselves — no third-party payment. Clinics that are **conventionné / tiers-payant** instead bill CNAM directly in batches via a bordereau récapitulatif, optionally by télétransmission. This is a separate pathway, deliberately out of scope for all current CNAM features.

## Current State
No conventionné/tiers-payant support. The bulletin is an individual printable BS1 for the patient to file.

## Expected State (only if the clinic operates conventionné)
- A clinic setting to enable the conventionné pathway.
- A periodic **bordereau récapitulatif** of bulletins for batch submission to CNAM.
- (Stretch) télétransmission to CNAM.

## Why Deferred
Not applicable to filière privée (the current target). This is genuinely a different product pathway, likely its own full `/define-feature`, not a small increment.

## Suggested Approach
- Scope as its own feature (`/define-feature`) when/if a conventionné clinic is onboarded. Confirm the current CNAM bordereau format + télétransmission channel with CNAM before building.
- Reference: CNAM "Espace Professionnel de santé" (cnam.nat.tn/espace_ps.jsp).

## Acceptance Criteria
- [ ] (When built) conventionné mode is gated behind an explicit clinic setting; filière-privée default unchanged.
- [ ] Bordereau récapitulatif generated in the current CNAM-accepted format.
