import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:klevo/main.dart';
import 'package:klevo/theme.dart';

void main() {
  test('Тема Klevo использует Ciridae-цвета', () {
    final theme = AppTheme.dark();
    expect(theme.scaffoldBackgroundColor, AppColors.canvas);
    expect(theme.colorScheme.brightness, Brightness.dark);
  });

  testWidgets('Приложение стартует с нижней навигацией',
      (WidgetTester tester) async {
    await tester.pumpWidget(const KlevoApp());
    await tester.pump();

    expect(find.text('ЖУРНАЛ'), findsOneWidget);
    expect(find.text('КАРТА'), findsOneWidget);
    expect(find.text('ЛЕНТА'), findsOneWidget);
    expect(find.text('НАСТРОЙКИ'), findsOneWidget);
  });
}
