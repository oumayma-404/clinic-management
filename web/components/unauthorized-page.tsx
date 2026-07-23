"use client"

import { useRouter } from "next/navigation"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Building2, Lock, ArrowRight, Plus, LogOut } from "lucide-react"

export default function UnauthorizedPage() {
  const router = useRouter()

  return (
    <div className="min-h-screen bg-gradient-to-br from-blue-50 via-white to-slate-50 dark:from-slate-950 dark:to-slate-900 flex items-center justify-center p-6">
      <div className="w-full max-w-md">
        <Card className="border-red-100 dark:border-red-900/20 shadow-lg">
          <CardHeader className="text-center space-y-4">
            <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-red-100 dark:bg-red-900/20 mx-auto">
              <Lock className="w-8 h-8 text-red-600 dark:text-red-400" />
            </div>
            <div>
              <CardTitle className="text-2xl text-red-900 dark:text-red-100">Accès restreint</CardTitle>
              <CardDescription className="mt-2">
                Vous devez faire partie d'une clinique pour accéder à cette application.
              </CardDescription>
            </div>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="p-4 bg-blue-50 dark:bg-blue-950/20 rounded-lg border border-blue-200 dark:border-blue-800">
              <p className="text-sm text-blue-900 dark:text-blue-100">
                Pour commencer, vous pouvez créer une nouvelle clinique ou en rejoindre une existante à l'aide d'un code de clinique.
              </p>
            </div>

            <div className="flex flex-col gap-3">
              <Button
                onClick={() => router.push("/setup")}
                className="w-full bg-blue-600 hover:bg-blue-700"
                size="lg"
              >
                <Plus className="w-4 h-4 mr-2" />
                Créer une clinique
              </Button>

              <Button
                onClick={() => router.push("/join")}
                variant="outline"
                className="w-full border-blue-200 hover:bg-blue-50 dark:hover:bg-blue-950/20"
                size="lg"
              >
                <Building2 className="w-4 h-4 mr-2" />
                Rejoindre une clinique
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
                Si vous pensez qu'il s'agit d'une erreur, veuillez contacter l'administrateur de votre clinique.
              </p>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}


