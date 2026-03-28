import * as React from "react";
import { useAuth } from "react-oidc-context";
import { useNavigate } from "react-router-dom";
import { Box, CircularProgress, Typography } from "@mui/material";

/**
 * Landing page for the Cognito Hosted UI redirect.
 * react-oidc-context processes the ?code= automatically on mount;
 * this component just shows a spinner and redirects once done.
 */
export default function CallbackPage() {
	const auth = useAuth();
	const navigate = useNavigate();

	React.useEffect(() => {
		if (!auth.isLoading && !auth.error) {
			navigate("/", { replace: true });
		}
	}, [auth.isLoading, auth.error, navigate]);

	if (auth.error) {
		return (
			<Box sx={{ textAlign: "center", py: 8 }}>
				<Typography color="error">
					Sign-in error: {auth.error.message}
				</Typography>
			</Box>
		);
	}

	return (
		<Box sx={{ display: "flex", justifyContent: "center", py: 8 }}>
			<CircularProgress />
		</Box>
	);
}
