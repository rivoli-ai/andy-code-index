// @ts-check
const eslint = require('@eslint/js');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

module.exports = tseslint.config(
  // Global ignores (must be its own config object with only `ignores`): keep
  // build output and vendor bundles out of linting.
  { ignores: ['dist/**', '.angular/**', 'node_modules/**', 'coverage/**'] },
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.recommended,
      ...angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'app', style: 'camelCase' },
      ],
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: 'app', style: 'kebab-case' },
      ],
      // Forbid debug-style console use (story #261) but keep warn/error, which are
      // legitimate last-resort logging.
      'no-console': ['error', { allow: ['warn', 'error'] }],
      // Pre-existing debt is surfaced as warnings to be ratcheted down, not as a
      // wall of blocking errors on first ESLint introduction (story #261).
      '@typescript-eslint/no-explicit-any': 'warn',
      '@typescript-eslint/no-unused-vars': 'warn',
      // Type-aware rule; would require wiring parserOptions.projectService. Off
      // for now to keep lint fast and self-contained (story #261).
      '@typescript-eslint/no-redundant-type-constituents': 'off',
      'no-empty': 'warn',
      'prefer-const': 'warn',
    },
  },
  {
    files: ['**/*.spec.ts'],
    rules: {
      'no-console': 'off',
      '@typescript-eslint/no-explicit-any': 'off',
    },
  },
  {
    files: ['**/*.html'],
    extends: [
      ...angular.configs.templateRecommended,
      ...angular.configs.templateAccessibility,
    ],
    // Template accessibility/correctness findings are real but pre-existing;
    // keep them visible as warnings while they are worked down (story #261).
    rules: {
      '@angular-eslint/template/click-events-have-key-events': 'warn',
      '@angular-eslint/template/interactive-supports-focus': 'warn',
      '@angular-eslint/template/label-has-associated-control': 'warn',
      '@angular-eslint/template/eqeqeq': 'warn',
      '@angular-eslint/template/no-negated-async': 'warn',
    },
  },
);
