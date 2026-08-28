import Foundation

/**
 Every user-facing string of the shell, in French — the language of the app it renders. There is no English set and
 no default-locale fallback to one, deliberately: an untranslated string appearing on a Tunisian dentist's phone is
 the defect, not the missing translation. The mirror of `mobile/android/…/res/values/strings.xml`, kept as Swift
 constants so a missing key is a compile error rather than a blank label.

 ⚠️ No string here names the « réseau local ». The same server is reached over a LAN, over Wi-Fi and over a mobile
 network, so that wording is false everywhere but the offline-LAN install — the rule `web/`'s
 `local-network-wording` gate enforces on the other side of the same product.
 */
enum Strings {

    static let appName = "APEXA"

    // State: connecting
    static let connectingTitle = "Connexion au serveur du cabinet…"

    // State: server address
    static let configSubtitle = "Adresse du serveur du cabinet"
    static let configHelp = """
        Saisissez l'adresse IP ou le nom du serveur (par ex. 192.168.1.10 ou clinic-server). \
        Le port 5001 est utilisé si vous n'en indiquez pas.
        """
    static let configPlaceholder = "192.168.1.10"
    static let configInvalid = "Veuillez saisir une adresse de serveur valide."
    static let configSave = "Se connecter"
    static let configCancel = "Annuler"

    // State: unreachable
    static let unreachableTitle = "Impossible de joindre le serveur du cabinet"
    static let unreachableRetry = "Réessayer"
    static let unreachableChangeServer = "Changer de serveur"

    static func unreachableDetail(address: String, reason: String) -> String {
        """
        Adresse : \(address)
        Détail : \(reason)

        Vérifiez que le serveur est allumé et accessible, puis réessayez.
        """
    }

    // State: update required
    static let updateTitle = "Mise à jour requise"
    static let updateOpenStore = "Mettre à jour"
    static let updateDetailNoFloor =
        "Ce serveur ne prend plus en charge cette version de l'application. Mettez-la à jour pour continuer."

    static func updateDetail(floor: String, installed: String) -> String {
        "Ce serveur demande la version \(floor) ou plus récente de l'application. La version installée est \(installed)."
    }

    // The « Serveur » actions, reached by shaking the device at the root of the app
    static let menuTitle = "Serveur"
    static let menuReload = "Recharger"
    static let menuChangeServer = "Changer de serveur…"

    // Bridge: handing a file to the OS
    static let saveFileFailed = "Le fichier n'a pas pu être enregistré sur cet appareil."
    static let saveFileNoViewer = "Aucune application de cet appareil ne peut ouvrir ce fichier."

    // Bridge: confirming the device owner so a paused session can resume (AC-57)
    static let identityDescription =
        "Confirmez votre identité pour revenir à votre travail sans ressaisir votre mot de passe."

    // External navigation
    static let externalOpenFailed = "Cette page n'a pas pu être ouverte sur cet appareil."

    static let close = "Fermer"
}
