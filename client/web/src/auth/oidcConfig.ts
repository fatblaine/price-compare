import type { AuthProviderProps } from "react-oidc-context";

// Fill these in via .env (REACT_APP_* prefix required for Create React App).
// In production, set them as Lambda / S3-hosting environment variables.
const region = process.env.REACT_APP_COGNITO_REGION ?? "ap-southeast-2";
const userPoolId = process.env.REACT_APP_COGNITO_USER_POOL_ID ?? "";
const clientId = process.env.REACT_APP_COGNITO_APP_CLIENT_ID ?? "";
// Hosted UI domain without protocol, e.g. "price-compare-auth.auth.ap-southeast-2.amazoncognito.com"
const cognitoDomain = process.env.REACT_APP_COGNITO_DOMAIN ?? "";

export const oidcConfig: AuthProviderProps = {
	authority: `https://cognito-idp.${region}.amazonaws.com/${userPoolId}`,
	client_id: clientId,
	redirect_uri: `${window.location.origin}/callback`,
	post_logout_redirect_uri: `${window.location.origin}/`,
	response_type: "code",
	scope: "openid email profile",
};

// Cognito logout URL — used by App.tsx handleLogout instead of signoutRedirect().
// Cognito does not fully implement the OIDC end_session_endpoint spec,
// so we construct the logout URL manually.
export function buildCognitoLogoutUrl(): string {
	const logoutUri = encodeURIComponent(`${window.location.origin}/`);
	return `https://${cognitoDomain}/logout?client_id=${clientId}&logout_uri=${logoutUri}`;
}
