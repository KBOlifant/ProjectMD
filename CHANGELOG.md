# What has changed in Project MD

A plain-language list of what was added and why. Newest first. Each item matches one commit.

## Coming next

- **Fetching real web pages and keeping AI cost low.** The server will open any public page with Playwright (a real browser), strip it down to the useful text, headings, links and tech clues, and only then hand a small summary to the AI. The tech list and page outline come from plain code, so the AI is only used for the written overview. Repeated pages are remembered so they cost nothing, each scan and each person has a daily AI limit, and the server falls back to a free basic document if the AI is off or the limit is reached.
- **Later, not started:** writing the documentation with the Groq AI model (the API key will live only on the server), and a way to give people the admin or viewer role.

## Website and server connected

- **Connected the website to the server.** The website can now run in two modes. In the default mode it works on its own with sample data stored in the browser, exactly as before. In "server mode" (turn it on with `NEXT_PUBLIC_SCAN_PROVIDER=api` in `frontend/.env.local`, and start the backend) scans, history and the monthly allowance come from the server and follow the person to any device. The website sends the person's Firebase sign-in with each request and shows plain messages when something goes wrong ("Verify your email to continue", "You've used this month's scans", "Can't reach the server"). The History page shows a Retry button if loading fails, and no longer flashes "No scans yet" while loading. *Why:* so scans belong to the person, not the browser. Tested against a stand-in server, not the real one yet.

## Backend server

- **Created the database tables and checked the real server.** The users and scans tables now exist in your Aiven PostgreSQL database, created from the setup script. The server was started against it: the health check reports the database as up, requests without a valid sign-in are refused, and your website address (port 3000) is allowed to talk to it. Nothing was saved yet because nobody has signed in through it.
- **Stopped "clear history" from resetting the monthly limit.** Clearing history now hides scans instead of erasing them. Hidden scans disappear from the person's list but still count toward the 10 scans a month, so nobody can create and clear over and over to get around the limit. This matters once each scan costs real money to generate. The database setup script was rewritten to include this (it had not been run yet). About 20 new tests check it.
- **Added the scans part of the server.** The server can now create a scan, list a person's scans, open one, and clear their history. Each person only ever sees their own scans (someone else's scan looks like it does not exist). Before anything is saved the web address is checked and refused if it points at a private or internal machine, so the app cannot be used to poke around inside a network. Each person gets 10 scans a month, and a limit on how fast they can start them. The scan content is still the made-up sample data, produced in one place so the real AI (Groq) can replace it later. *Why:* history and limits have to live on the server, not in the browser. About 185 automatic tests cover the address checks, limits and privacy between users.
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
