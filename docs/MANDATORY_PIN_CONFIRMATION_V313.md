# Mandatory overdue PIN replacement — V2026.09.313

The fixed 560-pixel dialog height clipped the footer buttons. The dialog now sizes vertically to its content, with a work-area height limit and vertical scrolling for smaller screens or longer model/error text.

When the opening count is at or above the threshold, replacement confirmation is mandatory: Cancel is hidden and its cancel action disabled, the window has no title-bar close button, Escape/Alt+F4 are handled, and the Closing event rejects dismissal until the reset callback reports success. An incorrect settings password, an active product/removal interlock or a failed save leaves the dialog open and the count unchanged. Successful password-authorized reset permits closing. Manual replacement below the threshold retains its existing cancel behavior.

Regression coverage opens the actual 10/10 WPF dialog using temporary credentials/SQLite and a fake board. It checks rejection of a wrong password and Close/Escape, absence of Cancel/title-bar close, full confirmation-button bounds, and closure only after the current settings password successfully resets the counter. A temporary render verifies the complete footer and validation message. No real production database or credential file is changed.
