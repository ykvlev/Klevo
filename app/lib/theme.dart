import 'package:flutter/material.dart';

/// Дизайн-система Ciridae: тёмный канвас, карточки #272a2a, акцент #cc6437.
class AppColors {
  static const canvas = Color(0xFF0B0B0B);
  static const card = Color(0xFF272A2A);
  static const cardRaised = Color(0xFF2E3232);
  static const hairline = Color(0xFF3A3D3D);
  static const accent = Color(0xFFCC6437);
  static const accentDim = Color(0x33CC6437);
  static const textPrimary = Color(0xFFF5F2EC);
  static const textSecondary = Color(0xFFB0AEAB);
  static const textMuted = Color(0xFF6F6D6A);
  static const ok = Color(0xFF4CAF50);
  static const warn = Color(0xFFE0A03C);
  static const bad = Color(0xFFE05C48);
}

class AppTheme {
  static ThemeData dark() {
    final base = ThemeData(
      useMaterial3: true,
      brightness: Brightness.dark,
      colorScheme: ColorScheme.fromSeed(
        seedColor: AppColors.accent,
        brightness: Brightness.dark,
        surface: AppColors.canvas,
      ),
      scaffoldBackgroundColor: AppColors.canvas,
      fontFamily: 'Inter',
    );

    return base.copyWith(
      textTheme: base.textTheme.copyWith(
        displaySmall: const TextStyle(
          fontFamily: 'BarlowCondensed',
          fontWeight: FontWeight.w700,
          letterSpacing: 0.5,
          fontSize: 28,
          color: AppColors.textPrimary,
        ),
        titleLarge: const TextStyle(
          fontFamily: 'BarlowCondensed',
          fontWeight: FontWeight.w600,
          fontSize: 22,
          color: AppColors.textPrimary,
        ),
        titleMedium: const TextStyle(
          fontWeight: FontWeight.w600,
          fontSize: 16,
          color: AppColors.textPrimary,
        ),
        bodyMedium: const TextStyle(
          fontSize: 14,
          color: AppColors.textPrimary,
        ),
        bodySmall: const TextStyle(
          fontSize: 12,
          color: AppColors.textSecondary,
        ),
        labelLarge: const TextStyle(
          fontWeight: FontWeight.w700,
          fontSize: 13,
          letterSpacing: 1.2,
          color: AppColors.textPrimary,
        ),
      ),
      appBarTheme: const AppBarTheme(
        backgroundColor: AppColors.canvas,
        foregroundColor: AppColors.textPrimary,
        elevation: 0,
        centerTitle: false,
      ),
      cardTheme: const CardThemeData(
        color: AppColors.card,
        elevation: 0,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.all(Radius.circular(10)),
        ),
        margin: EdgeInsets.zero,
      ),
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: AppColors.card,
        indicatorColor: AppColors.accentDim,
        height: 68,
        labelTextStyle: WidgetStatePropertyAll(
          const TextStyle(fontSize: 11, letterSpacing: 0.5, color: AppColors.textSecondary),
        ),
        iconTheme: WidgetStateProperty.resolveWith(
          (states) => IconThemeData(
            color: states.contains(WidgetState.selected) ? AppColors.accent : AppColors.textMuted,
          ),
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: AppColors.card,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(10),
          borderSide: const BorderSide(color: AppColors.hairline),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(10),
          borderSide: const BorderSide(color: AppColors.hairline),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(10),
          borderSide: const BorderSide(color: AppColors.accent),
        ),
        hintStyle: const TextStyle(color: AppColors.textMuted),
      ),
      dividerTheme: const DividerThemeData(color: AppColors.hairline, thickness: 1),
      snackBarTheme: const SnackBarThemeData(
        backgroundColor: AppColors.cardRaised,
        contentTextStyle: TextStyle(color: AppColors.textPrimary),
        behavior: SnackBarBehavior.floating,
      ),
    );
  }
}
