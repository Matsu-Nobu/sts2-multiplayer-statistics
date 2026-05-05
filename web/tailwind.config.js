/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{svelte,ts}'],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        bg: {
          0: '#0b0d10',
          1: '#13161b',
          2: '#1b1f26',
          3: '#252a33',
        },
        accent: {
          DEFAULT: '#7aa2f7',
          dim: '#3b4a6b',
        },
        ok: '#9ece6a',
        bad: '#f7768e',
        warn: '#e0af68',
      },
    },
  },
  plugins: [],
};
