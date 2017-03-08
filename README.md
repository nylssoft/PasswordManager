# PasswordManager

A simple password manager for Windows using WPF.

The passwords for each item are encrypted with the Rijndael algorithm using an encrypted key file (also with Rijndael).
Preferable the key file should be stored on a different device than the password file (e.g. a USB stick).
The key file is encrypted using the SHA-256 hash of the master password.
The process itself tries to keep the plain text passwords in memory as short as possible. The SecureString class is used to store passwords in memory for the life time of the process.

Features:

- Copy login and password to clibboard and clear clibboard after 30 seconds
- Simple password generator
- Open website for login
- Thumbnails for login websites if available
- Secure password storage in memory and in encrypted files
- Change of master password and key file
- Available for German and English

TODOs:

- Add LICENSE.TXT file (may be GPLv3, do not know by now)
- A new project name
- A filter for the items in the list view
- A mechansim to add arbitary encrypted files to an entry (e.g. scanned invoices or bank account information)

Icons used from the Open Icon Library (https://sourceforge.net/projects/openiconlibrary):

application-exit-5.png / nuovext2 / LGPL-2.1
document-decrypt-3.png / oxygen / CC-BY-SA 3.0 or LGPL
document-encrypt-3.png / oxygen / CC-BY-SA 3.0 or LGPL
document-new-6.ico / oxygen / CC-BY-SA 3.0 or LGPL
document-open-2.png / echo / CC-BY-SA-3.0
document-properties-2.png / gnome / GPLv2
document-save-7.png / nuovext2 / LGPL-2.1
document-save-as-6.png / nuovext2 / LGPL-2.1
edit.png / gimp / GPLv2
homepage.png / nuvola / LGPL-2.1
key.png / famfamfam-silk / CC-BY-2.5 or CC-BY-3.0
kgpg_info.png / nuvola / LGPL-2.1
list-add-4.ico / oxygen / CC-BY-SA 3.0 or LGPL
list-remove-4.ico / oxygen / CC-BY-SA 3.0 or LGPL


