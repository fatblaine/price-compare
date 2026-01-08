import * as React from "react";
import {
	Alert,
	Box,
	Button,
	Card,
	CardContent,
	CircularProgress,
	Stack,
	TextField,
	Typography,
} from "@mui/material";
import { useTheme, useMediaQuery } from "@mui/material";
import { login } from "./api/auth";

export interface LoginPageProps {
	onLoggedIn?: () => void;
	onSwitchToRegister?: () => void;
	onGuestLogin?: () => void;
}

export default function LoginPage(props: LoginPageProps) {
	const { onLoggedIn, onSwitchToRegister, onGuestLogin } = props;
	const theme = useTheme();
	const isXs = useMediaQuery(theme.breakpoints.down("sm"));

	const [email, setEmail] = React.useState("");
	const [password, setPassword] = React.useState("");
	const [loading, setLoading] = React.useState(false);
	const [error, setError] = React.useState<string | null>(null);
	const [successMessage, setSuccessMessage] = React.useState<string | null>(
		null,
	);

	const handleSubmit: React.FormEventHandler<HTMLFormElement> = async (e) => {
		e.preventDefault();
		setError(null);
		setSuccessMessage(null);

		if (!email.trim() || !password.trim()) {
			setError("Email and password are required");
			return;
		}

		setLoading(true);
		try {
			await login(email.trim(), password);
			setSuccessMessage("Login successful.");
			if (onLoggedIn) {
				onLoggedIn();
			}
		} catch (err: any) {
			const serverMsg: string | undefined =
				err?.response?.data && typeof err.response.data === "string"
					? err.response.data
					: err?.message;
			setError(serverMsg || "Operation failed. Please try again.");
		} finally {
			setLoading(false);
		}
	};

	const title = "Sign in";
	const submitLabel = "Sign in";

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
							<Typography
								variant={isXs ? "h5" : "h4"}
								fontWeight={800}
							>
								{title}
							</Typography>
							<Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
								Use your email and password to access Price-Compare.
							</Typography>
						</Box>

						{error && (
							<Alert severity="error" variant="outlined">
								{error}
							</Alert>
						)}

						{successMessage && !error && (
							<Alert severity="success" variant="outlined">
								{successMessage}
							</Alert>
						)}

						<Box component="form" onSubmit={handleSubmit} noValidate>
							<Stack spacing={2.5}>
								<TextField
									label="Email"
									type="email"
									fullWidth
									required
									autoComplete="email"
									value={email}
									onChange={(e) => setEmail(e.target.value)}
								/>
								<TextField
									label="Password"
									type="password"
									fullWidth
									required
									autoComplete="current-password"
									value={password}
									onChange={(e) => setPassword(e.target.value)}
								/>

								<Button
									type="submit"
									variant="contained"
									size="large"
									disabled={loading}
									sx={{
										mt: 1,
										py: 1.2,
										fontWeight: 600,
										textTransform: "none",
										background:
											"linear-gradient(90deg, #0ea5e9 0%, #6366f1 50%, #a855f7 100%)",
									}}
									fullWidth
								>
									{loading ? (
										<CircularProgress size={22} sx={{ color: "white" }} />
									) : (
										submitLabel
									)}
								</Button>
							</Stack>
						</Box>

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

						{onSwitchToRegister && (
							<Button
								variant="text"
								onClick={onSwitchToRegister}
								sx={{ textTransform: "none", fontWeight: 500 }}
							>
								Don't have an account? Create one
							</Button>
						)}
					</Stack>
				</CardContent>
			</Card>
		</Box>
	);
}
