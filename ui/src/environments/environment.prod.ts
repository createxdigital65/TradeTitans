// Trade Titans - Production Environment Configuration
// This file is used for production builds (ng build / ng build --configuration production)
//
// IMPORTANT: Replace 'https://YOUR_MONSTERASP_DOMAIN' with your actual MonsterASP.NET
// deployment domain AFTER your hosting is provisioned.
//
// Example:
//   apiBaseUrl: 'https://tradetitans.azurewebsites.net/api'
//   or
//   apiBaseUrl: 'https://your-app.monsterasp.net/api'
//
// You can also override this at build time using environment variables:
//   ng build --configuration production --define "window.__API_BASE_URL__='https://your-domain/api'"

export const environment = {
  production: true,
  apiBaseUrl: 'https://YOUR_MONSTERASP_DOMAIN/api',
  appName: 'Trade Titans'
};
