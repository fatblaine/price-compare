import {
	AppBar,
	Box,
	Button,
	CircularProgress,
	Container,
	Dialog,
	DialogActions,
	DialogContent,
	DialogContentText,
	DialogTitle,
	Tab,
	Tabs,
	Toolbar,
	Typography,
} from "@mui/material";
import React from "react";
import {
	Navigate,
	Route,
	Routes,
	useLocation,
	useNavigate,
} from "react-router-dom";
import { useAuth } from "react-oidc-context";
import axios from "axios";
import ProductsPage from "./ProductsPage";
import LoginPage from "./LoginPage";
import CallbackPage from "./CallbackPage";
import { clearGuestSession, getStoredGuest, setGuestSession } from "./api/auth";
import { buildCognitoLogoutUrl } from "./auth/oidcConfig";
import { fetchAdminHealth } from "./api/admin";
import MyReceiptsPage from "./MyReceiptsPage";
import MyFavoritesPage from "./MyFavoritesPage";
import AdminJobsPage from "./AdminJobsPage";

function App() {
	const auth = useAuth();
	const [guestMode, setGuestMode] = React.useState(getStoredGuest);
	const [isAdmin, setIsAdmin] = React.useState(false);
	const [restrictedDialogOpen, setRestrictedDialogOpen] = React.useState(false);
	const [pendingProtectedTab, setPendingProtectedTab] = React.useState<
		"receipts" | "favorites" | "admin" | null
	>(null);
	const location = useLocation();
	const navigate = useNavigate();

	const isAuthenticated = auth.isAuthenticated;
	const userEmail = auth.user?.profile?.email as string | undefined;
	const isGuest = guestMode && !isAuthenticated;
	const showTabs = isAuthenticated || isGuest;

	// Keep axios Authorization header in sync with the OIDC id_token
	React.useEffect(() => {
		const token = auth.user?.id_token;
		if (token) {
			axios.defaults.headers.common.Authorization = `Bearer ${token}`;
			clearGuestSession();
			setGuestMode(false);
		} else if (!auth.isLoading) {
			// eslint-disable-next-line @typescript-eslint/no-dynamic-delete
			delete axios.defaults.headers.common.Authorization;
		}
	}, [auth.user, auth.isLoading]);

	// Check admin status whenever auth changes
	React.useEffect(() => {
		let active = true;
		if (!isAuthenticated) {
			setIsAdmin(false);
			return;
		}
		(async () => {
			try {
				await fetchAdminHealth(1);
				if (active) setIsAdmin(true);
			} catch {
				if (active) setIsAdmin(false);
			}
		})();
		return () => {
			active = false;
		};
	}, [isAuthenticated]);

	const mainTab = React.useMemo(() => {
		const path = location.pathname.toLowerCase();
		if (path.startsWith("/admin")) return "admin";
		if (path.startsWith("/receipts")) return "receipts";
		if (path.startsWith("/favorites")) return "favorites";
		return "products";
	}, [location.pathname]);

	const adminDefaultPath = "/admin/schedules";
	const adminPath = location.pathname.startsWith("/admin/")
		? location.pathname
		: adminDefaultPath;
	const tabToPath = (
		tab: "products" | "receipts" | "favorites" | "admin",
	) => (tab === "admin" ? adminDefaultPath : `/${tab}`);

	const handleLogout = async () => {
		clearGuestSession();
		setGuestMode(false);
		await auth.removeUser(); // Clear OIDC session locally
		window.location.href = buildCognitoLogoutUrl(); // Redirect to Cognito logout
	};

	const handleGuestLogin = () => {
		setGuestSession();
		setGuestMode(true);
		navigate("/products", { replace: true });
		setPendingProtectedTab(null);
	};

	const handleTabChange = (
		_: React.SyntheticEvent,
		val: "products" | "receipts" | "favorites" | "admin",
	) => {
		if (val === "admin" && !isAdmin) return;
		if (val !== "products" && !isAuthenticated) {
			setPendingProtectedTab(val);
			setRestrictedDialogOpen(true);
			return;
		}
		const targetPath = val === "admin" ? adminPath : `/${val}`;
		navigate(targetPath);
	};

	const handleCloseRestrictedDialog = () => {
		setRestrictedDialogOpen(false);
		setPendingProtectedTab(null);
	};

	const handleRestrictedSignIn = () => {
		setRestrictedDialogOpen(false);
		auth.signinRedirect();
	};

	const restrictedTarget =
		pendingProtectedTab === "receipts"
			? "My Receipts"
			: pendingProtectedTab === "favorites"
				? "My Favorites"
				: pendingProtectedTab === "admin"
					? "Admin"
					: "this section";

	const renderMainContent = () => {
		// /callback must always be reachable (Cognito redirects here after login)
		if (location.pathname === "/callback") {
			return <CallbackPage />;
		}

		// OIDC is still initialising (e.g. processing the auth code on page load)
		if (auth.isLoading) {
			return (
				<Box sx={{ display: "flex", justifyContent: "center", py: 8 }}>
					<CircularProgress />
				</Box>
			);
		}

		// Not signed in and not a guest → show login card
		if (!isAuthenticated && !isGuest) {
			return <LoginPage onGuestLogin={handleGuestLogin} />;
		}

		return (
			<Routes>
				<Route path="/" element={<Navigate to="/products" replace />} />
				<Route path="/callback" element={<CallbackPage />} />
				<Route path="/products" element={<ProductsPage />} />
				<Route
					path="/receipts"
					element={
						isAuthenticated ? (
							<MyReceiptsPage />
						) : (
							<Navigate to="/products" replace />
						)
					}
				/>
				<Route
					path="/favorites"
					element={
						isAuthenticated ? (
							<MyFavoritesPage />
						) : (
							<Navigate to="/products" replace />
						)
					}
				/>
				<Route
					path="/admin/*"
					element={
						isAuthenticated && isAdmin ? (
							<AdminJobsPage />
						) : (
							<Navigate to="/products" replace />
						)
					}
				/>
				<Route path="*" element={<Navigate to="/products" replace />} />
			</Routes>
		);
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
						sx={{
							display: "flex",
							alignItems: { xs: "flex-start", md: "center" },
							flexDirection: { xs: "column", md: "row" },
						}}
					>
						<Typography
							variant="h4"
							component="h1"
							sx={{
								flexGrow: { xs: 0, md: 1 },
								fontWeight: 800,
								letterSpacing: 0.5,
								textShadow: "0 1px 2px rgba(0,0,0,.25)",
								fontSize: { xs: 22, sm: 26, md: 30, lg: 34 },
								textAlign: { xs: "center", sm: "left" },
								width: { xs: "100%", md: "auto" },
							}}
						>
							Price-peer
						</Typography>
						{showTabs && (
							<Tabs
								value={mainTab}
								onChange={handleTabChange}
								textColor="inherit"
								indicatorColor="secondary"
								sx={{
									ml: { xs: 0, md: 2 },
									mt: { xs: 1, md: 0 },
									minHeight: "auto",
									width: { xs: "100%", md: "auto" },
									justifyContent: { xs: "center", md: "flex-start" },
									"& .MuiTab-root": {
										minHeight: "auto",
										fontSize: { xs: 12, sm: 13, md: 14 },
									},
								}}
							>
								<Tab label="Products" value="products" />
								<Tab label="My Receipts" value="receipts" />
								<Tab label="My Favorites" value="favorites" />
								{isAuthenticated && isAdmin && (
									<Tab label="Admin" value="admin" />
								)}
							</Tabs>
						)}
						{isAuthenticated && userEmail && (
							<Typography
								variant="body2"
								sx={{
									ml: { xs: 0, md: 2 },
									mt: { xs: 1, md: 0 },
									fontWeight: 500,
									maxWidth: { xs: 140, sm: 200 },
									overflow: "hidden",
									textOverflow: "ellipsis",
									whiteSpace: "nowrap",
									textShadow: "0 1px 1px rgba(0,0,0,0.25)",
								}}
							>
								{userEmail}
							</Typography>
						)}
						{isGuest && (
							<Typography
								variant="body2"
								sx={{
									ml: { xs: 0, md: 2 },
									mt: { xs: 1, md: 0 },
									fontWeight: 500,
									textShadow: "0 1px 1px rgba(0,0,0,0.25)",
								}}
							>
								Guest access
							</Typography>
						)}
						{isAuthenticated && (
							<Button
								color="inherit"
								onClick={handleLogout}
								sx={{
									fontWeight: 500,
									ml: { xs: 0, md: 2 },
									mt: { xs: 1, md: 0 },
								}}
							>
								Log out
							</Button>
						)}
						{(isGuest || (!isAuthenticated && !auth.isLoading)) && (
							<Button
								color="inherit"
								onClick={() => auth.signinRedirect()}
								sx={{
									fontWeight: 500,
									ml: { xs: 0, md: 2 },
									mt: { xs: 1, md: 0 },
								}}
							>
								Sign in
							</Button>
						)}
					</Container>
				</Toolbar>
			</AppBar>

			<Container maxWidth="lg" sx={{ py: { xs: 2, md: 3 } }}>
				{renderMainContent()}
			</Container>

			<Dialog
				open={restrictedDialogOpen}
				onClose={handleCloseRestrictedDialog}
				aria-labelledby="signin-required-title"
				aria-describedby="signin-required-description"
			>
				<DialogTitle id="signin-required-title">Sign in required</DialogTitle>
				<DialogContent>
					<DialogContentText id="signin-required-description">
						{restrictedTarget} is only available after signing in. Guests can
						view products only.
					</DialogContentText>
				</DialogContent>
				<DialogActions>
					<Button onClick={handleCloseRestrictedDialog}>Keep browsing</Button>
					<Button variant="contained" onClick={handleRestrictedSignIn}>
						Sign in
					</Button>
				</DialogActions>
			</Dialog>
		</Box>
	);
}

export default App;
