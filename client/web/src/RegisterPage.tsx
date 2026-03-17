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
import { register, verifyEmail } from "./api/auth";

export interface RegisterPageProps {
	onLoggedIn?: () => void;
	onSwitchToLogin?: () => void;
}

export default function RegisterPage(props: RegisterPageProps) {
	const { onLoggedIn, onSwitchToLogin } = props;
	const theme = useTheme();
	const isXs = useMediaQuery(theme.breakpoints.down("sm"));

	// Step 1: register form
	const [email, setEmail] = React.useState("");
	const [password, setPassword] = React.useState("");

	// Step 2: OTP verification
	const [step, setStep] = React.useState<1 | 2>(1);
	const [otp, setOtp] = React.useState("");

	const [loading, setLoading] = React.useState(false);
	const [error, setError] = React.useState<string | null>(null);

	const handleRegister: React.FormEventHandler<HTMLFormElement> = async (e) => {
		e.preventDefault();
		setError(null);

		if (!email.trim() || !password.trim()) {
			setError("Email and password are required");
			return;
		}

		setLoading(true);
		try {
			await register(email.trim(), password);
			setStep(2);
		} catch (err: any) {
			const serverMsg: string | undefined =
				err?.response?.data?.message ?? err?.message;
			setError(serverMsg || "Registration failed. Please try again.");
		} finally {
			setLoading(false);
		}
	};

	const handleVerify: React.FormEventHandler<HTMLFormElement> = async (e) => {
		e.preventDefault();
		setError(null);

		if (otp.length !== 6) {
			setError("Please enter the 6-digit code from your email.");
			return;
		}

		setLoading(true);
		try {
			await verifyEmail(email.trim(), otp);
			if (onLoggedIn) {
				onLoggedIn();
			}
		} catch (err: any) {
			const serverMsg: string | undefined =
				err?.response?.data?.message ?? err?.message;
			setError(serverMsg || "Verification failed. Please try again.");
		} finally {
			setLoading(false);
		}
	};

	const handleResend = async () => {
		setError(null);
		setOtp("");
		setLoading(true);
		try {
			await register(email.trim(), password);
		} catch {
			// Silently ignore resend errors — OTP may still be valid
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
						{step === 1 ? (
							<>
								<Box>
									<Typography variant={isXs ? "h5" : "h4"} fontWeight={800}>
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

								<Box component="form" onSubmit={handleRegister} noValidate>
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
							</>
						) : (
							<>
								<Box>
									<Typography variant={isXs ? "h5" : "h4"} fontWeight={800}>
										Verify your email
									</Typography>
									<Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
										We sent a 6-digit code to <strong>{email}</strong>. It expires in 15 minutes.
									</Typography>
								</Box>

								{error && (
									<Alert severity="error" variant="outlined">
										{error}
									</Alert>
								)}

								<Box component="form" onSubmit={handleVerify} noValidate>
									<Stack spacing={2.5}>
										<TextField
											label="Verification code"
											fullWidth
											required
											autoComplete="one-time-code"
											inputProps={{ maxLength: 6, inputMode: "numeric", pattern: "[0-9]*" }}
											value={otp}
											onChange={(e) =>
												setOtp(e.target.value.replace(/\D/g, "").slice(0, 6))
											}
											placeholder="123456"
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
												"Verify"
											)}
										</Button>
									</Stack>
								</Box>

								<Stack direction="row" spacing={1} alignItems="center">
									<Typography variant="body2" color="text.secondary">
										Didn't receive it?
									</Typography>
									<Button
										variant="text"
										size="small"
										disabled={loading}
										onClick={() => void handleResend()}
										sx={{ textTransform: "none", fontWeight: 500, p: 0, minWidth: 0 }}
									>
										Resend code
									</Button>
								</Stack>

								<Button
									variant="text"
									size="small"
									onClick={() => { setStep(1); setError(null); setOtp(""); }}
									sx={{ textTransform: "none", fontWeight: 500 }}
								>
									← Back to registration
								</Button>
							</>
						)}
					</Stack>
				</CardContent>
			</Card>
		</Box>
	);
}
