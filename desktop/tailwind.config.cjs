module.exports = {
  content: ["./src/**/*.{ts,tsx}"],
  presets: [require("nativewind/preset")],
  darkMode: "class",
  theme: {
    extend: {
      colors: Object.fromEntries(
        ["background", "surface", "ink", "muted", "line", "thyme", "timber", "on-thyme"]
          .map(name => [name, `rgb(var(--${name}) / <alpha-value>)`])
      ),
    },
  },
  plugins: [],
};
