"use client"

import { useRouter } from "next/navigation"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Building2, Lock, ArrowRight, Plus, LogOut } from "lucide-react"

export default function UnauthorizedPage() {
  const router = useRouter()

  return (
    /*
     * The page ground is `--background`, not a gradient. This is one of the three screens a new clinic sees
     * before it has an account, and the gradient it carried (`via-white … dark:from-slate-950`) was the app's
     * only surface maintaining its own light/dark pair by hand — so it neither matched the themed chrome
     * behind it nor followed the user's theme correctly.
     */
    <div className="min-h-dvh bg-background flex items-center justify-center p-6">
      <div className="w-full max-w-md">
        <Card className="border-destructive/25 shadow-lg">
          <CardHeader className="text-center space-y-4">
            <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-destructive-wash mx-auto">
              <Lock className="w-8 h-8 text-destructive" />
            </div>
            <div>
              <CardTitle className="text-2xl text-destructive">Accès restreint</CardTitle>
              <CardDescription className="mt-2">
                Vous devez faire partie d'un cabinet pour accéder à cette application.
              </CardDescription>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="p-4 bg-accent/20 rounded-lg border border-primary/25">
              <p className="text-sm text-accent-foreground">
                Pour commencer, vous pouvez créer un nouveau cabinet ou en rejoindre un existant à l'aide d'un code de cabinet.
              </p>
            </div>

            <div className="flex flex-col gap-3">
              <Button
                onClick={() => router.push("/setup")}
                className="w-full bg-primary hover:bg-primary/90"
                size="lg"
              >
                <Plus className="w-4 h-4 mr-2" />
                Créer un cabinet
              </Button>

              <Button
                onClick={() => router.push("/join")}
                variant="outline"
                className="w-full border-primary/25 hover:bg-accent/20"
                size="lg"
              >
                <Building2 className="w-4 h-4 mr-2" />
                Rejoindre un cabinet
                <ArrowRight className="w-4 h-4 ml-2" />
              </Button>
            </div>

            <div className="pt-4 border-t space-y-3">
              <Button
                variant="ghost"
                onClick={() => window.location.href = "/auth/logout"}
                className="w-full text-muted-foreground hover:text-destructive"
                size="sm"
              >
                <LogOut className="w-4 h-4 mr-2" />
                Se déconnecter
              </Button>
              <p className="text-xs text-muted-foreground text-center">
                Si vous pensez qu'il s'agit d'une erreur, veuillez contacter l'administrateur de votre cabinet.
              </p>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}


