// Proxies /api/* to the Aspire-managed API service. The AppHost injects
// `services__api__http__0` (and `https__0`) via WithReference + WithEnvironment.
const target =
  process.env.services__api__https__0 ||
  process.env.services__api__http__0 ||
  'http://localhost:5000';

module.exports = [
  {
    context: ['/api'],
    target,
    secure: false,
    changeOrigin: true,
    logLevel: 'debug',
  },
];
