import {
	AppBar,
	Box,
	Button,
	Container,
	Tab,
	Tabs,
	Toolbar,
	Typography,
} from "@mui/material";
import React from "react";
import ProductsPage from "./ProductsPage";
import LoginPage from "./LoginPage";
import RegisterPage from "./RegisterPage";
import { clearToken, getStoredToken } from "./api/auth";
import MyReceiptsPage from "./MyReceiptsPage";
import MyFavoritesPage from "./MyFavoritesPage";

function App() {
	const [token, setToken] = React.useState<string | null>(null);
	const [authView, setAuthView] = React.useState<"login" | "register">("login");
	const [mainTab, setMainTab] = React.useState<"products" | "receipts" | "favorites">("products");

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

	const renderMainContent = () => {
		if (!isAuthenticated) {
			return authView === "login" ? (
				<LoginPage
					onLoggedIn={handleLoggedIn}
					onSwitchToRegister={() => setAuthView("register")}
				/>
			) : (
				<RegisterPage
					onLoggedIn={handleLoggedIn}
					onSwitchToLogin={() => setAuthView("login")}
				/>
			);
		}

		if (mainTab === "receipts") {
			return <MyReceiptsPage />;
		}

		if (mainTab === "favorites") {
			return <MyFavoritesPage />;
		}

		return <ProductsPage />;
	};

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
							<Tabs
								value={mainTab}
								onChange={(_, val) => setMainTab(val)}
								textColor="inherit"
								indicatorColor="secondary"
								sx={{
									ml: 2,
									minHeight: "auto",
									"& .MuiTab-root": { minHeight: "auto" },
									display: { xs: "none", md: "flex" },
								}}
							>
								<Tab label="Products" value="products" />
								<Tab label="My Receipts" value="receipts" />
								<Tab label="My Favorites" value="favorites" />
							</Tabs>
						)}
						{isAuthenticated && (
							<Button
								color="inherit"
								onClick={handleLogout}
								sx={{ fontWeight: 500, ml: { xs: 0, md: 2 } }}
							>
								Log out
							</Button>
						)}
					</Container>
				</Toolbar>
			</AppBar>

			<Container maxWidth="lg" sx={{ py: { xs: 2, md: 3 } }}>
				{renderMainContent()}
			</Container>
		</Box>
	);
}

export default App;
