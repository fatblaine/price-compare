// Auth is now handled by AWS Cognito via react-oidc-context.
// This file retains only the guest-session helpers used by App.tsx.

const GUEST_KEY = "pc_guest";

export function setGuestSession(): void {
	if (typeof window === "undefined") return;
	localStorage.setItem(GUEST_KEY, "1");
}

export function clearGuestSession(): void {
	if (typeof window === "undefined") return;
	localStorage.removeItem(GUEST_KEY);
}

export function getStoredGuest(): boolean {
	if (typeof window === "undefined") return false;
	return localStorage.getItem(GUEST_KEY) === "1";
}
