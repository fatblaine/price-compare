import * as React from "react";
import {
	Box,
	Button,
	Card,
	CardContent,
	Stack,
	Typography,
} from "@mui/material";
import { useTheme, useMediaQuery } from "@mui/material";
import { useAuth } from "react-oidc-context";

export interface LoginPageProps {
	onGuestLogin?: () => void;
}

export default function LoginPage({ onGuestLogin }: LoginPageProps) {
	const auth = useAuth();
	const theme = useTheme();
	const isXs = useMediaQuery(theme.breakpoints.down("sm"));

	return (
		<Box
			sx={{
				minHeight: "60vh",
				display: "flex",
				alignItems: "center",
				justifyContent: "center",
				py: { xs: 4, md: 6 },
			}}
		>
			<Card
				variant="outlined"
				sx={{
					width: "100%",
					maxWidth: 420,
					borderRadius: 3,
					boxShadow: "0 18px 45px rgba(15, 23, 42, 0.18)",
					borderColor: "divider",
				}}
			>
				<CardContent sx={{ p: { xs: 3, sm: 4 } }}>
					<Stack spacing={3}>
						<Box>
							<Typography variant={isXs ? "h5" : "h4"} component="h2" fontWeight={800}>
								Welcome to Price-peer
							</Typography>
							<Typography
								variant="body2"
								color="text.secondary"
								sx={{ mt: 0.5 }}
							>
								Sign in or create an account to compare grocery prices.
							</Typography>
						</Box>

						<Button
							variant="contained"
							size="large"
							onClick={() => auth.signinRedirect()}
							sx={{
								py: 1.2,
								fontWeight: 600,
								textTransform: "none",
								background:
									"linear-gradient(90deg, #0ea5e9 0%, #6366f1 50%, #a855f7 100%)",
							}}
							fullWidth
						>
							Sign in / Register
						</Button>

						{onGuestLogin && (
							<Stack spacing={1} sx={{ textAlign: "center" }}>
								<Typography variant="body2" color="text.secondary">
									Continue as a guest to browse products. Favorites and receipts
									require a full sign-in.
								</Typography>
								<Button
									variant="outlined"
									onClick={onGuestLogin}
									sx={{ textTransform: "none", fontWeight: 600 }}
								>
									Continue as guest
								</Button>
							</Stack>
						)}
					</Stack>
				</CardContent>
			</Card>
		</Box>
	);
}
