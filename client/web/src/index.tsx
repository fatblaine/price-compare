import React from "react";
import ReactDOM from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { AuthProvider } from "react-oidc-context";
import "./index.css";
import "driver.js/dist/driver.css";
import App from "./App";
import { oidcConfig } from "./auth/oidcConfig";
import reportWebVitals from "./reportWebVitals";

const root = ReactDOM.createRoot(
	document.getElementById("root") as HTMLElement,
);

root.render(
	<React.StrictMode>
		<BrowserRouter>
			<AuthProvider
				{...oidcConfig}
				onSigninCallback={() => {
					// Remove the ?code=&state= params from the URL after OIDC processes them
					window.history.replaceState(
						{},
						document.title,
						window.location.pathname,
					);
				}}
			>
				<App />
			</AuthProvider>
		</BrowserRouter>
	</React.StrictMode>,
);

reportWebVitals();
