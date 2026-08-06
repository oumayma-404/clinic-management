import UIKit

/**
 The tokens the shell's own screens use, converted from `web/app/globals.css` so the states that render *before*
 the web app loads do not look like a different product than the one that follows them. Byte-identical to
 `mobile/android/…/res/values/colors.xml`.
 */
enum ShellColors {
    static let primary = UIColor(red: 0x00 / 255, green: 0x73 / 255, blue: 0x6B / 255, alpha: 1)
    static let onPrimary = UIColor(red: 0xF8 / 255, green: 0xFD / 255, blue: 0xFC / 255, alpha: 1)
    static let surface = UIColor(red: 0xF4 / 255, green: 0xF6 / 255, blue: 0xF8 / 255, alpha: 1)
    static let ink = UIColor(red: 0x1F / 255, green: 0x29 / 255, blue: 0x33 / 255, alpha: 1)
    static let inkMuted = UIColor(red: 0x52 / 255, green: 0x60 / 255, blue: 0x6D / 255, alpha: 1)
    static let danger = UIColor(red: 0xC0 / 255, green: 0x39 / 255, blue: 0x2B / 255, alpha: 1)
}

/**
 The shape every pre-web state shares: a centred column that scrolls when it does not fit.

 ⚠️ **Centred with the scroll view's own content inset, never by pinning the stack to the centre.** A stack
 centred inside a scroll view pushes its overflow to *both* ends and the top overflow lands outside the scrollable
 region — the same vertical clipping trap `web/`'s § 11 documents, which on a landscape phone made the top of a
 card unreachable by any means. Here the stack is pinned to the top and the *inset* does the centring, so it
 degrades to top-aligned the moment there is no free space.
 */
class ShellPanel: UIView {

    let stack = UIStackView()
    private let scrollView = UIScrollView()

    init() {
        super.init(frame: .zero)
        backgroundColor = ShellColors.surface

        scrollView.translatesAutoresizingMaskIntoConstraints = false
        scrollView.alwaysBounceVertical = true
        scrollView.keyboardDismissMode = .interactive
        addSubview(scrollView)

        stack.translatesAutoresizingMaskIntoConstraints = false
        stack.axis = .vertical
        stack.alignment = .fill
        stack.spacing = 16
        scrollView.addSubview(stack)

        NSLayoutConstraint.activate([
            scrollView.leadingAnchor.constraint(equalTo: safeAreaLayoutGuide.leadingAnchor),
            scrollView.trailingAnchor.constraint(equalTo: safeAreaLayoutGuide.trailingAnchor),
            scrollView.topAnchor.constraint(equalTo: safeAreaLayoutGuide.topAnchor),
            scrollView.bottomAnchor.constraint(equalTo: safeAreaLayoutGuide.bottomAnchor),

            stack.topAnchor.constraint(equalTo: scrollView.contentLayoutGuide.topAnchor, constant: 24),
            stack.bottomAnchor.constraint(equalTo: scrollView.contentLayoutGuide.bottomAnchor, constant: -24),
            stack.leadingAnchor.constraint(equalTo: scrollView.frameLayoutGuide.leadingAnchor, constant: 24),
            stack.trailingAnchor.constraint(equalTo: scrollView.frameLayoutGuide.trailingAnchor, constant: -24),
        ])
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("not used — this shell has no storyboards") }

    override func layoutSubviews() {
        super.layoutSubviews()
        let free = scrollView.bounds.height - stack.frame.height - 48
        let top = max(0, free / 2)
        // Only when it actually changes: assigning `contentInset` triggers another layout pass, and an
        // unconditional write here is an infinite loop rather than a centred card.
        if abs(scrollView.contentInset.top - top) > 0.5 {
            scrollView.contentInset = UIEdgeInsets(top: top, left: 0, bottom: 0, right: 0)
        }
    }

    // MARK: - Builders shared by every state

    static func title(_ text: String) -> UILabel {
        let label = UILabel()
        label.text = text
        label.textColor = ShellColors.ink
        // A pixel size would ignore the user's text-size setting; `preferredFont` follows Dynamic Type.
        label.font = UIFont.preferredFont(forTextStyle: .title2)
        label.adjustsFontForContentSizeCategory = true
        label.numberOfLines = 0
        label.textAlignment = .center
        return label
    }

    static func body(_ text: String, muted: Bool = true) -> UILabel {
        let label = UILabel()
        label.text = text
        label.textColor = muted ? ShellColors.inkMuted : ShellColors.ink
        label.font = UIFont.preferredFont(forTextStyle: .body)
        label.adjustsFontForContentSizeCategory = true
        label.numberOfLines = 0
        label.textAlignment = .center
        return label
    }

    /// 44 pt is the floor a finger needs, and every control in this shell is operated by one.
    static func button(_ title: String, primary: Bool) -> UIButton {
        let button = UIButton(type: .system)
        button.setTitle(title, for: .normal)
        button.titleLabel?.font = UIFont.preferredFont(forTextStyle: .headline)
        button.titleLabel?.adjustsFontForContentSizeCategory = true
        button.backgroundColor = primary ? ShellColors.primary : .clear
        button.setTitleColor(primary ? ShellColors.onPrimary : ShellColors.primary, for: .normal)
        button.layer.cornerRadius = 10
        button.layer.borderWidth = primary ? 0 : 1
        button.layer.borderColor = ShellColors.primary.cgColor
        button.heightAnchor.constraint(greaterThanOrEqualToConstant: 44).isActive = true
        return button
    }
}

/// State: connecting. Names the address it is reaching, so a wrong one is visible before it fails.
final class ConnectingPanel: ShellPanel {

    let targetLabel = ShellPanel.body("")

    override init() {
        super.init()
        let spinner = UIActivityIndicatorView(style: .large)
        spinner.color = ShellColors.primary
        spinner.startAnimating()
        stack.addArrangedSubview(spinner)
        stack.addArrangedSubview(ShellPanel.title(Strings.connectingTitle))
        stack.addArrangedSubview(targetLabel)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("not used — this shell has no storyboards") }
}

/// State: server address (AC-17). The address is typed here and nowhere else — never compiled in.
final class ConfigPanel: ShellPanel {

    let addressField = UITextField()
    let errorLabel = ShellPanel.body(Strings.configInvalid)
    let saveButton = ShellPanel.button(Strings.configSave, primary: true)
    let cancelButton = ShellPanel.button(Strings.configCancel, primary: false)

    override init() {
        super.init()

        addressField.placeholder = Strings.configPlaceholder
        addressField.borderStyle = .roundedRect
        addressField.font = UIFont.preferredFont(forTextStyle: .body)
        addressField.adjustsFontForContentSizeCategory = true
        addressField.autocapitalizationType = .none
        addressField.autocorrectionType = .no
        addressField.spellCheckingType = .no
        addressField.keyboardType = .URL
        addressField.returnKeyType = .go
        addressField.clearButtonMode = .whileEditing
        addressField.heightAnchor.constraint(greaterThanOrEqualToConstant: 44).isActive = true

        errorLabel.textColor = ShellColors.danger
        errorLabel.isHidden = true

        stack.addArrangedSubview(ShellPanel.title(Strings.appName))
        stack.addArrangedSubview(ShellPanel.body(Strings.configSubtitle, muted: false))
        stack.addArrangedSubview(addressField)
        stack.addArrangedSubview(errorLabel)
        stack.addArrangedSubview(ShellPanel.body(Strings.configHelp))
        stack.addArrangedSubview(saveButton)
        stack.addArrangedSubview(cancelButton)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("not used — this shell has no storyboards") }
}

/// States: unreachable and update-required. Same shape, different buttons — both name the cause (AC-15).
final class MessagePanel: ShellPanel {

    let detailLabel = ShellPanel.body("")
    private(set) var buttons: [UIButton] = []

    init(title: String, buttonTitles: [String]) {
        super.init()
        stack.addArrangedSubview(ShellPanel.title(title))
        stack.addArrangedSubview(detailLabel)
        for (index, buttonTitle) in buttonTitles.enumerated() {
            let button = ShellPanel.button(buttonTitle, primary: index == 0)
            buttons.append(button)
            stack.addArrangedSubview(button)
        }
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("not used — this shell has no storyboards") }
}
