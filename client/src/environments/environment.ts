export const environment = {
  production: false,
  apiUrl: '/api/v1',
  auth: {
    authority: 'https://localhost:5001',
    clientId: 'andy-code-index-web',
    redirectUri: 'https://localhost:4201/callback',
    scope: 'openid profile email urn:andy-code-index-api offline_access',
  }
};
