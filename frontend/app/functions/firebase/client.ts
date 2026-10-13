import { getApp, getApps, initializeApp, type FirebaseApp } from "firebase/app";
import { connectAuthEmulator, getAuth, type Auth } from "firebase/auth";

// Only this folder and app/functions/auth import "firebase/*". UI code goes through the auth service.
// No Analytics SDK is used on purpose.

const EMULATOR_URL = "http://127.0.0.1:9099";

// Reads the Firebase web config from the NEXT_PUBLIC_FIREBASE_* variables in .env.local. Each
// variable is written out in full so Next can inline it into the browser bundle. If any is
// missing it throws one error naming that variable. Stores nothing.
function readConfig() {
  const entries: [string, string | undefined][] = [
    ["NEXT_PUBLIC_FIREBASE_API_KEY", process.env.NEXT_PUBLIC_FIREBASE_API_KEY],
    ["NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN", process.env.NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN],
    ["NEXT_PUBLIC_FIREBASE_PROJECT_ID", process.env.NEXT_PUBLIC_FIREBASE_PROJECT_ID],
    ["NEXT_PUBLIC_FIREBASE_STORAGE_BUCKET", process.env.NEXT_PUBLIC_FIREBASE_STORAGE_BUCKET],
    ["NEXT_PUBLIC_FIREBASE_MESSAGING_SENDER_ID", process.env.NEXT_PUBLIC_FIREBASE_MESSAGING_SENDER_ID],
    ["NEXT_PUBLIC_FIREBASE_APP_ID", process.env.NEXT_PUBLIC_FIREBASE_APP_ID],
  ];
  for (const [name, value] of entries) {
    if (!value) {
      throw new Error(`Missing environment variable ${name}. Add it to frontend/.env.local (see .env.example).`);
    }
  }
  const [apiKey, authDomain, projectId, storageBucket, messagingSenderId, appId] = entries.map(
    ([, value]) => value as string,
  );
  return { apiKey, authDomain, projectId, storageBucket, messagingSenderId, appId };
}

// Returns the single Firebase app, creating it on first use (the getApps() guard also survives
// hot reload). Stores nothing itself; Firebase keeps the signed-in session in the browser.
function getFirebaseApp(): FirebaseApp {
  return getApps().length > 0 ? getApp() : initializeApp(readConfig());
}

// Returns the Firebase Auth instance, setting it up lazily and only once. Importing this module
// never touches Firebase, so SSR and `next build` are safe; calling this function does, so it is
// browser-only and throws a clear error if a config variable is missing. When
// NEXT_PUBLIC_USE_AUTH_EMULATOR is "true" it points Auth at the local emulator (once).
export function getFirebaseAuth(): Auth {
  if (typeof window === "undefined") {
    throw new Error("getFirebaseAuth() is browser-only. Call it from an event handler or effect, not during render on the server.");
  }
  const auth = getAuth(getFirebaseApp());
  if (process.env.NEXT_PUBLIC_USE_AUTH_EMULATOR === "true" && !auth.emulatorConfig) {
    connectAuthEmulator(auth, EMULATOR_URL);
  }
  return auth;
}
