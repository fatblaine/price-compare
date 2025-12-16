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
import { login, register } from "./api/auth";

export interface RegisterPageProps {
	onLoggedIn?: () => void;
	onSwitchToLogin?: () => void;
}

export default function RegisterPage(props: RegisterPageProps) {
	const { onLoggedIn, onSwitchToLogin } = props;
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
			await register(email.trim(), password);
			setSuccessMessage("Registration successful. Signing you in...");
			await login(email.trim(), password);
			if (onLoggedIn) {
				onLoggedIn();
			}
		} catch (err: any) {
			const serverMsg: string | undefined =
				err?.response?.data && typeof err.response.data === "string"
					? err.response.data
					: err?.message;
			setError(serverMsg || "Registration failed. Please try again.");
		} finally {
			setLoading(false);
		}
	};

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
								Create account
							</Typography>
							<Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
								Register with your email to start comparing prices.
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
									autoComplete="new-password"
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
										"Register"
									)}
								</Button>
							</Stack>
						</Box>

						{onSwitchToLogin && (
							<Button
								variant="text"
								onClick={onSwitchToLogin}
								sx={{ textTransform: "none", fontWeight: 500 }}
							>
								Already have an account? Sign in
							</Button>
						)}
					</Stack>
				</CardContent>
			</Card>
		</Box>
	);
}

