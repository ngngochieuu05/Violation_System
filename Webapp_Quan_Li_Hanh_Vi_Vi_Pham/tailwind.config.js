/** @type {import('tailwindcss').Config} */
module.exports = {
    darkMode: 'class',
    content: [
        "./Views/**/*.cshtml",
        "./Areas/**/*.cshtml", /* BẮT BUỘC PHẢI CÓ DÒNG NÀY ĐỂ QUÉT FILE ADMIN */
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