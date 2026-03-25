export const environment = {
  production: true,
  apiUrl: '/api/v1',
  auth: {
    authority: '',  // Set at deploy time
    clientId: 'andy-code-index-web',
    redirectUri: '', // Set at deploy time
    scope: 'openid profile email urn:andy-code-index-api offline_access',
  }
};
