using Helpdesk.Web.Models;

namespace Helpdesk.Web.Data;

/// <summary>Fictional knowledge-base content for "Lakeview Components", a made-up company.</summary>
public static class SeedKbArticles
{
    public static IReadOnlyList<KbArticle> All { get; } = new List<KbArticle>
    {
        new()
        {
            Slug = "reset-your-network-password",
            Title = "Reset your network password",
            Category = TicketCategory.Access,
            Tags = "password reset locked account expired login sign-in",
            Summary = "Use the self-service portal to reset an expired or forgotten network password.",
            Body = """
                Network passwords expire every 90 days. You get a reminder email 7 days before expiry.

                To reset a forgotten or expired password:
                - Open the self-service portal at https://passwords.lakeview.example from any device.
                - Choose "Forgot password" and enter your work email address.
                - Approve the prompt in your authenticator app.
                - Pick a new password with at least 14 characters. You cannot reuse your last 10 passwords.

                After a reset, sign out and back in on your laptop, phone mail app and VPN client so they pick up the new password. Old saved passwords on a phone are the most common reason an account locks again.

                If the account is locked after 5 failed attempts it unlocks automatically after 15 minutes. Raise a ticket in the Access category if it stays locked.
                """,
        },
        new()
        {
            Slug = "set-up-multi-factor-authentication",
            Title = "Set up multi-factor authentication (MFA)",
            Category = TicketCategory.Access,
            Tags = "mfa 2fa authenticator app new phone sign-in verification code",
            Summary = "Register the authenticator app, and move it when you change phones.",
            Body = """
                Every account must use the authenticator app as a second factor. SMS codes are only allowed as a temporary fallback.

                First-time setup:
                - Install the authenticator app from your phone's app store.
                - Sign in at https://account.lakeview.example/security and choose "Add sign-in method".
                - Scan the QR code with the app and approve the test notification.

                Changing phones: add the new phone as a sign-in method before you wipe the old one. If the old phone is already gone, raise an Access ticket; an agent verifies your identity by video call and issues a one-time temporary access pass valid for 8 hours.
                """,
        },
        new()
        {
            Slug = "connect-to-the-vpn",
            Title = "Connect to the VPN from home",
            Category = TicketCategory.Network,
            Tags = "vpn remote work home disconnects drops connection client tunnel",
            Summary = "Install the VPN client, connect, and fix the common causes of dropped connections.",
            Body = """
                The VPN client is pre-installed on company laptops. Open it from the system tray and choose the "Lakeview-Main" gateway.

                If the VPN keeps disconnecting:
                - Switch from Wi-Fi to a wired connection, or move closer to the router. Most drops are caused by weak Wi-Fi.
                - Make sure the laptop is not set to sleep the network adapter: Settings > Power > "Allow the computer to turn off this device" must be unchecked.
                - Update the client from the Software Center; versions older than 5.2 drop the tunnel every 60 minutes.
                - Try the backup gateway "Lakeview-DR" if the main one is slow.

                The VPN uses your network password and MFA. If you recently changed your password, reconnect with the new one.
                """,
        },
        new()
        {
            Slug = "office-wifi-networks",
            Title = "Office Wi-Fi networks and guest access",
            Category = TicketCategory.Network,
            Tags = "wifi wireless guest visitor network internet office slow",
            Summary = "Which Wi-Fi network to use in the office and how to give visitors access.",
            Body = """
                Use "Lakeview-Staff" on company laptops and phones. It signs in automatically with your device certificate.

                Visitors use "Lakeview-Guest". Generate a guest code at https://guest.lakeview.example; codes last 24 hours and allow 3 devices.

                Personal devices must use the guest network. They cannot join the staff network.

                If Wi-Fi is slow in one meeting room, report the room name in a Network ticket so the access point can be checked.
                """,
        },
        new()
        {
            Slug = "add-a-network-printer",
            Title = "Add a network printer",
            Category = TicketCategory.Hardware,
            Tags = "printer printing print queue toner paper jam scanner",
            Summary = "Install an office printer and clear stuck print jobs.",
            Body = """
                Printers are named by floor and location, for example "PRN-2F-EAST".

                To add a printer:
                - Open Settings > Printers & scanners > Add device.
                - Pick the printer named on the label on the device. If it does not appear, connect to the staff Wi-Fi or VPN first.

                If jobs are stuck in the queue, open the queue, cancel all documents, then restart the "Print Spooler" service from the Software Center "Fix printing" tool.

                For paper jams or empty toner, use the facilities request form; IT does not stock toner.
                """,
        },
        new()
        {
            Slug = "request-new-or-replacement-laptop",
            Title = "Request a new or replacement laptop",
            Category = TicketCategory.Hardware,
            Tags = "laptop computer replacement broken screen battery hardware new starter",
            Summary = "How laptop requests, repairs and the four-year refresh work.",
            Body = """
                Laptops are refreshed every four years. You get an email when yours is due.

                For a broken laptop (cracked screen, battery swelling, keyboard failure) raise a Hardware ticket with the asset tag from the sticker under the laptop. A loan laptop is available the same day from the IT desk on floor 1.

                New starter laptops are requested by the hiring manager at least 5 working days before the start date.

                Back up local files to your personal cloud drive before handing in a laptop. Devices are wiped on return.
                """,
        },
        new()
        {
            Slug = "set-up-email-on-your-phone",
            Title = "Set up work email on your phone",
            Category = TicketCategory.Email,
            Tags = "email phone mobile outlook mail app sync calendar",
            Summary = "Use the approved mail app on iOS or Android and fix sync problems.",
            Body = """
                Only the approved mail app may be used for work email on phones. The built-in phone mail apps are blocked.

                Setup:
                - Install the approved mail app and sign in with your work email.
                - Accept the device management prompt; it only manages work data in the app.

                If mail stops syncing after a password change, remove the account from the app and add it again. If the app says the device is not compliant, update the phone's operating system.
                """,
        },
        new()
        {
            Slug = "shared-mailbox-access",
            Title = "Get access to a shared mailbox",
            Category = TicketCategory.Email,
            Tags = "shared mailbox access permission team inbox email delegate",
            Summary = "Shared mailbox access needs approval from the mailbox owner.",
            Body = """
                Each shared mailbox has an owner, usually a team lead. Access requests need their approval.

                Raise an Email ticket with the mailbox address and the access level you need: "read" or "send as". The agent asks the owner to approve in the ticket.

                After access is granted, the mailbox appears in your mail client within about an hour. Restart the mail client if it does not.
                """,
        },
        new()
        {
            Slug = "install-approved-software",
            Title = "Install approved software from the Software Center",
            Category = TicketCategory.Software,
            Tags = "install software application program license admin rights software center",
            Summary = "Self-install approved apps; request review for anything else.",
            Body = """
                Staff do not have administrator rights. Approved applications are installed from the Software Center on your laptop without a ticket.

                For software that is not listed, raise a Software ticket with the product name, the business reason and whether it needs a paid licence. Security reviews new software within 5 working days.

                Licensed products such as design or statistics tools need cost-centre approval from your manager.
                """,
        },
        new()
        {
            Slug = "fix-video-meeting-audio",
            Title = "Fix audio and camera problems in video meetings",
            Category = TicketCategory.Software,
            Tags = "meeting audio microphone camera headset echo video call sound",
            Summary = "Check devices, permissions and the headset before raising a ticket.",
            Body = """
                Most meeting audio problems are caused by the wrong device being selected.

                Before you join, open the meeting app's device settings and pick your headset for both speaker and microphone. Use the "test call" option.

                If others hear an echo, someone in the call is using laptop speakers with an open microphone; ask them to use a headset.

                If the camera is not detected, check that the privacy shutter is open and that Settings > Privacy > Camera allows the meeting app. Meeting-room equipment problems go to the Hardware category with the room name.
                """,
        },
        new()
        {
            Slug = "request-access-to-shared-drives",
            Title = "Request access to shared drives and folders",
            Category = TicketCategory.Access,
            Tags = "shared drive folder access permission files department network drive",
            Summary = "Folder access is granted through group membership approved by the data owner.",
            Body = """
                Department folders are on the shared drive and access is controlled by groups, not by individual permissions.

                Raise an Access ticket with the full folder path and whether you need read or edit access. The folder's data owner approves the request in the ticket.

                Access appears after you sign out and back in. If you can see the folder but get "access denied" on files, mention that in the ticket; it usually means a sub-folder has its own permissions.
                """,
        },
        new()
        {
            Slug = "report-a-phishing-email",
            Title = "Report a suspicious or phishing email",
            Category = TicketCategory.Email,
            Tags = "phishing suspicious email spam scam security link attachment report",
            Summary = "Use the Report button; never forward suspicious mail to colleagues.",
            Body = """
                If an email asks you to sign in, pay an invoice, or open an unexpected attachment, treat it as suspicious.

                Use the "Report phishing" button in the mail client. It sends the message to the security team and removes it from your inbox.

                If you already clicked a link or entered your password, reset your password immediately using the self-service portal and raise an urgent Access ticket so the security team can review sign-in activity.
                """,
        },
    };
}
