# What has changed in Project MD

A plain-language list of what was added and why. Newest first. Each item matches one commit.

## Coming next

- **Saved scans on the server.** Creating, listing and clearing scans will go through the server and be stored in your PostgreSQL database instead of the browser, so history follows the person, not the device.
- **Safe web address checks.** The server will refuse addresses that point at private or internal machines, so nobody can use the app to poke around inside a network.
- **Monthly scan limit.** The server will count each person's scans per month and stop at the limit (10 for now).
- **Website connected to the server.** The website will talk to the server using the person's Firebase sign-in, and show friendly messages if something goes wrong.
- **Later, not started:** writing the documentation with the Groq AI model (the API key will live only on the server), and a way to give people the admin or viewer role.

## Backend server

- **Added sign-in checks and role rules to the server.** The server now checks that every request comes from someone signed in with Firebase, and applies the same viewer / member / admin rules as the website (starting a scan needs a verified email). A request with no valid sign-in is turned away, and a refused request says why (for example "verify your email"). There is also a "who am I" address (`/api/me`) that returns your email, role and scan usage. *Why:* the website's rules can be bypassed by anyone with browser tools, so the server has to enforce them too. About 130 automatic tests check the rules. Giving people the admin or viewer role is not possible yet.
- **Created the backend server project and database layout.** A C# server (ASP.NET Core) now exists in the `backend` folder. It describes two database tables, users and scans, and has a health-check address (`/api/health`). The database address and password are kept in a private setting on your computer, never in the code. The first database setup script is written but has not been run yet. *Why:* the website needs a real place to store accounts and scans.
- **Added instruction files for the backend server work.** Documents that tell the helper agents what to build on the server and how the website will talk to it. No app code.

## Sign-in and accounts

- **Show who is signed in and limit actions by role.** A menu shows your email, role and a sign-out button. People who have not verified their email see a banner and cannot start scans. Read-only users cannot start scans or clear history. A failed scan now shows what went wrong and a Try again button.
- **Added login, sign-up, forgot-password and verify-email pages.** New people can create an account, get a verification email, and reset a forgotten password. The pages never reveal whether an email already has an account. *Why:* scans should belong to a person.
- **Fixed a build error in the scraping test file.** The app failed to build because of one setting; raising it fixed the build. Nothing changed for users.
- **Kept scan data in one place and gave each user their own history.** The app now gets new scans from a single function, so the sample data can later be swapped for the real server. History is saved per account, so people sharing a browser no longer see each other's scans.
- **Added Firebase sign-in and split the app into signed-in and signed-out areas.** Email and password sign-in through your Firebase project. Pages that need an account send you to the login page when you are signed out.
- **Added user roles and permissions.** Viewer, member and admin, with a rule that starting a scan needs a verified email. New users are members. Unknown roles are treated as viewer, the safest choice.
- **Added instruction files for the sign-in and login work.** Documents for the helper agents. No app code.

## Earlier

- The website screens (Generate, Processing, Results, History), the design, and a first web-scraping test.
