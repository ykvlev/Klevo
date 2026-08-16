import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'package:klevo/main.dart';

/// E2E: приложение стартует, подключается к API и отрисовывает прогноз.
/// Перед запуском: dotnet run --project backend/src/Klevo.Api --urls http://localhost:5178
/// Запуск: flutter test integration_test -d windows
void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('Приложение загружает прогноз и правила с API', (tester) async {
    await tester.pumpWidget(const KlevoApp());

    // ждём загрузку точек и прогноза (до 20 с)
    for (var i = 0; i < 40; i++) {
      if (find.text('ПРОГНОЗ КЛЁВА').evaluate().isNotEmpty) break;
      await tester.pump(const Duration(milliseconds: 500));
    }

    expect(find.text('ПРОГНОЗ КЛЁВА'), findsOneWidget,
        reason: 'Карточка прогноза не появилась — проверь, что API запущен '
            'на http://localhost:5178 и журнал открылся на точке');

    // список журнала ленивый — проскроллим вниз, чтобы показать правила и уловы
    await tester.drag(find.byType(ListView).first, const Offset(0, -500));
    await tester.pump(const Duration(milliseconds: 300));
    expect(find.textContaining('ПРАВИЛА'), findsWidgets);
    expect(find.textContaining('УЛОВЫ'), findsWidgets);

    // переход на карту
    await tester.tap(find.text('КАРТА'));
    await tester.pump(const Duration(seconds: 1));
    await tester.pump(const Duration(seconds: 1));
    expect(find.text('КАРТА ТОЧЕК'), findsOneWidget);

    // переход на ленту
    await tester.tap(find.text('ЛЕНТА'));
    await tester.pump(const Duration(seconds: 1));
    await tester.pump(const Duration(seconds: 1));
    expect(find.text('ЛЕНТА УЛОВОВ'), findsOneWidget);

    // переход на настройки
    await tester.tap(find.text('НАСТРОЙКИ'));
    await tester.pump(const Duration(milliseconds: 400));
    expect(find.text('АДРЕС API'), findsOneWidget);
  });
}
