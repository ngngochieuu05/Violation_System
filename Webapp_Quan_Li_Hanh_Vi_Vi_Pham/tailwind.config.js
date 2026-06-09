/** @type {import('tailwindcss').Config} */
module.exports = {
    darkMode: 'class', // Kích hoạt tính năng Dark Mode bằng class
    content: [
        "./Views/**/*.cshtml",
        "./Pages/**/*.cshtml",
        "./wwwroot/**/*.js"
    ],
    theme: {
        extend: {
            colors: {
                border: "shadow-sm",
            }
        },
    },
    plugins: [],
}