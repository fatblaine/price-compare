import {
	AppBar,
	Box,
	Button,
	Container,
	Toolbar,
	Typography,
} from "@mui/material";
import React from "react";
import ProductsPage from "./ProductsPage";
import LoginPage from "./LoginPage";
import RegisterPage from "./RegisterPage";
import { clearToken, getStoredToken } from "./api/auth";

function App() {
	const [token, setToken] = React.useState<string | null>(null);
	const [authView, setAuthView] = React.useState<"login" | "register">("login");

	React.useEffect(() => {
		const stored = getStoredToken();
		setToken(stored);
	}, []);

	const handleLoggedIn = () => {
		const stored = getStoredToken();
		setToken(stored);
		setAuthView("login");
	};

	const handleLogout = () => {
		clearToken();
		setToken(null);
	};

	const isAuthenticated = !!token;

	return (
		<Box sx={{ bgcolor: "background.default", minHeight: "100vh" }}>
			<AppBar
				position="sticky"
				elevation={0}
				sx={{
					background:
						"linear-gradient(90deg, #0ea5e9 0%, #6366f1 50%, #a855f7 100%)",
				}}
			>
				<Toolbar sx={{ py: { xs: 1, sm: 1.5 } }}>
					<Container
						maxWidth="lg"
						sx={{ display: "flex", alignItems: "center" }}
					>
						<Typography
							variant="h4"
							component="h1"
							sx={{
								flexGrow: 1,
								fontWeight: 800,
								letterSpacing: 0.5,
								textShadow: "0 1px 2px rgba(0,0,0,.25)",
								fontSize: { xs: 22, sm: 26, md: 30, lg: 34 },
								textAlign: { xs: "center", sm: "left" },
							}}
						>
							Price-Compare
						</Typography>
						{isAuthenticated && (
							<Button
								color="inherit"
								onClick={handleLogout}
								sx={{ fontWeight: 500 }}
							>
								Log out
							</Button>
						)}
					</Container>
				</Toolbar>
			</AppBar>

			<Container maxWidth="lg" sx={{ py: { xs: 2, md: 3 } }}>
				{isAuthenticated ? (
					<ProductsPage />
				) : authView === "login" ? (
					<LoginPage
						onLoggedIn={handleLoggedIn}
						onSwitchToRegister={() => setAuthView("register")}
					/>
				) : (
					<RegisterPage
						onLoggedIn={handleLoggedIn}
						onSwitchToLogin={() => setAuthView("login")}
					/>
				)}
			</Container>
		</Box>
	);
}

export default App;
